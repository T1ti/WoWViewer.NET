using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls.Primitives;
using Avalonia.Controls;
using Avalonia.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WTEditor.Application;
using WTEditor.Application.Commands;
using WTEditor.Application.Models;
using WTEditor.Application.Services;
using WTEditor.Avalonia.Services;
using WTEditor.Avalonia.Rendering;
using WTEditor.Avalonia.Presentation;
using WTEditor.Avalonia.Controls;
using WTEditor.Avalonia.ViewModels;
using WTEditor.Avalonia.Views;
using WoWRenderLib.DX11.Managers;
using WoWRenderLib.DX11.Objects;
using WoWRenderLib.DX11.Editing;
using WoWRenderLib.DX11.Raycasting;
using WoWRenderLib.DX11.Renderer;
using WoWRenderLib.DX11;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Loaders;
using WoWRenderLib.Raycasting;
using WoWRenderLib.Structs;
using WoWRenderLib.Services;

namespace WTEditor.Avalonia.Tests;

public sealed partial class EditorSettingsSmokeTests
{
    [TestMethod]
    public void TransformCommand_UpdatesDocumentRendererAndUndoHistory()
    {
        var document = new EditorDocument("Test map");
        var objectId = EditorObjectId.New();
        document.AddObject(new EditorObjectSnapshot(objectId, "Crate", "M2", ObjectTransform.Identity));
        document.MarkSaved();

        var expected = ObjectTransform.Identity with { Position = new Vector3(10, 20, 30) };
        var sink = new RecordingSceneSink();
        var history = new UndoService();

        history.Execute(new TransformObjectCommand(
            document,
            objectId,
            ObjectTransform.Identity,
            expected,
            sink));

        Assert.IsTrue(document.IsDirty);
        Assert.IsTrue(history.CanUndo);
        Assert.AreEqual(expected, GetObject(document, objectId).Transform);

        history.Undo();
        Assert.AreEqual(ObjectTransform.Identity, GetObject(document, objectId).Transform);
        Assert.IsTrue(history.CanRedo);

        history.Redo();
        Assert.AreEqual(expected, GetObject(document, objectId).Transform);
        Assert.AreEqual(3, sink.Updates.Count);
    }

    [TestMethod]
    public void UndoTransaction_GroupsDragUpdatesIntoOneHistoryEntry()
    {
        var value = 0;
        var history = new UndoService();

        using (var transaction = history.BeginTransaction("Drag object"))
        {
            history.Execute(new DelegateCommand("Step 1", () => value++, () => value--));
            history.Execute(new DelegateCommand("Step 2", () => value++, () => value--));
            transaction.Commit();
        }

        Assert.AreEqual(2, value);
        Assert.AreEqual("Drag object", history.UndoDescription);
        history.Undo();
        Assert.AreEqual(0, value);
        history.Redo();
        Assert.AreEqual(2, value);
    }

    [TestMethod]
    public void UndoTransaction_RollbackPreservesExistingRedoHistory()
    {
        var value = 1;
        var history = new UndoService();
        history.RecordExecuted(new DelegateCommand("Initial edit", () => value = 1, () => value = 0));
        history.Undo();

        using (history.BeginTransaction("Cancelled edit"))
        {
            history.Execute(new DelegateCommand("Temporary edit", () => value = 10, () => value = 0));
        }

        Assert.AreEqual(0, value);
        Assert.IsFalse(history.CanUndo);
        Assert.IsTrue(history.CanRedo);
        Assert.AreEqual("Initial edit", history.RedoDescription);

        history.Redo();
        Assert.AreEqual(1, value);
    }

    [TestMethod]
    public void UndoService_RecordExecutedDoesNotApplyLiveActionTwice()
    {
        var value = 1;
        var history = new UndoService();
        var command = new DelegateCommand("Live edit", () => value = 2, () => value = 1);

        value = 2;
        history.RecordExecuted(command);

        Assert.AreEqual(2, value);
        history.Undo();
        Assert.AreEqual(1, value);
        history.Redo();
        Assert.AreEqual(2, value);
    }

    [TestMethod]
    public void UndoService_FailedUndoPreservesHistoryEntry()
    {
        var history = new UndoService();
        history.RecordExecuted(new DelegateCommand(
            "Failing undo",
            () => { },
            () => throw new InvalidOperationException("Undo failed.")));

        Assert.ThrowsException<InvalidOperationException>(history.Undo);

        Assert.IsTrue(history.CanUndo);
        Assert.IsFalse(history.CanRedo);
        Assert.AreEqual("Failing undo", history.UndoDescription);
    }

    [TestMethod]
    public void UndoService_FailedRedoPreservesHistoryEntry()
    {
        var history = new UndoService();
        history.RecordExecuted(new DelegateCommand(
            "Failing redo",
            () => throw new InvalidOperationException("Redo failed."),
            () => { }));
        history.Undo();

        Assert.ThrowsException<InvalidOperationException>(history.Redo);

        Assert.IsFalse(history.CanUndo);
        Assert.IsTrue(history.CanRedo);
        Assert.AreEqual("Failing redo", history.RedoDescription);
    }

    [TestMethod]
    public void SelectionService_DeduplicatesAndTracksPrimaryObject()
    {
        var first = EditorObjectId.New();
        var second = EditorObjectId.New();
        var selection = new SelectionService();

        selection.Set([first, second, first], second);

        Assert.AreEqual(2, selection.Current.ObjectIds.Count);
        Assert.AreEqual(second, selection.Current.Primary);
        selection.Clear();
        Assert.AreEqual(0, selection.Current.ObjectIds.Count);
        Assert.IsNull(selection.Current.Primary);
    }

    [TestMethod]
    public void ToolManager_FailedActivationRestoresPreviousTool()
    {
        var manager = new ToolManager();
        var previous = new RecordingTool("previous");
        var failing = new RecordingTool("failing", failOnActivation: true);
        manager.Register(previous);
        manager.Register(failing);
        manager.Activate(previous.Id);
        var changes = 0;
        manager.ActiveToolChanged += (_, _) => changes++;

        Assert.ThrowsException<InvalidOperationException>(() => manager.Activate(failing.Id));

        Assert.AreSame(previous, manager.ActiveTool);
        Assert.AreEqual(2, previous.ActivationCount);
        Assert.AreEqual(1, previous.DeactivationCount);
        Assert.AreEqual(0, changes);
    }

    [TestMethod]
    public void SelectionInspector_ComposesTransformAndReusableModelInformation()
    {
        var inspector = new SelectionInspectorViewModel([]);
        var selected = new EditorObjectSnapshot(
            EditorObjectId.New(),
            "Training Dummy",
            "M2 model",
            new ObjectTransform(
                new Vector3(10, 20, 30),
                Quaternion.Identity,
                new Vector3(2)),
            new M2ObjectData(
                1234, 5678, 6, false,
                "world/model/dummy.m2",
                [new AssetReference(99, "textures/dummy.blp")],
                42,
                "world/maps/test/test_1_2.adt",
                new ModelAdvancedData(3, 100, 50, 2, 1, 8, 4),
                new MapPlacementData(MapPlacementKind.Mddf, 42, 0x21),
                [new ModelMaterialData(0, 2, 5, "", "Diffuse_T1", "Combiners_Mod",
                    [new AssetReference(99, "textures/dummy.blp")],
                    TextureSlots: [new ModelTextureData(1, new AssetReference(99, "textures/dummy.blp"), 3)])],
                [new ModelBatchData(0, null, 0, 12, 30, 2, 5, "", "Diffuse_T1", "Combiners_Mod", [new AssetReference(99, "textures/dummy.blp")])],
                [new ModelGeosetData(0, 101, "Hair", 0, 0, 100, 0, 30, true)],
                [new ModelTextureData(0, new AssetReference(99, "textures/dummy.blp"), 3)]));

        inspector.Inspect(selected);

        Assert.IsTrue(inspector.HasSelection);
        Assert.AreEqual("Training Dummy", inspector.SelectionName);
        Assert.AreEqual(10d, inspector.PositionX);
        Assert.IsTrue(inspector.ModelInformation.HasModel);
        Assert.AreEqual("world/model/dummy.m2", inspector.ModelInformation.ModelFile!.Path);
        Assert.AreEqual("world/model/dummy.m2", inspector.ModelInformation.ModelFile.ToolTip);
        Assert.AreEqual(0, inspector.ModelInformation.Properties.Count);
        Assert.AreEqual("Geosets (1)", inspector.ModelInformation.GeosetsHeader);
        Assert.AreEqual("Textures (1)", inspector.ModelInformation.TexturesHeader);
        Assert.AreEqual("Materials (1)", inspector.ModelInformation.MaterialsHeader);
        Assert.AreEqual("Render batches (1)", inspector.ModelInformation.BatchesHeader);
        Assert.AreEqual("Particle emitters", inspector.ModelInformation.AdvancedProperties[4].Label);
        Assert.AreEqual("Geoset 101 · Hair", inspector.ModelInformation.SelectedGeoset!.Name);
        Assert.AreEqual("Alpha blend (2)", inspector.ModelInformation.SelectedMaterial!.Properties[0].Value);
        Assert.AreEqual("Unlit, Two Sided", inspector.ModelInformation.SelectedMaterial.Flags!.DisplayText);
        Assert.AreEqual(ModelMaterialType.M2, inspector.ModelInformation.SelectedMaterial.MaterialType);
        Assert.AreEqual("textures/dummy.blp", inspector.ModelInformation.SelectedMaterial.Name);
        Assert.AreSame(inspector.ModelInformation.SelectedMaterial, inspector.ModelInformation.SelectedBatch!.Material);
        Assert.AreEqual("Wrap X, Wrap Y", inspector.ModelInformation.Textures[0].Flags!.DisplayText);
        Assert.AreEqual("Wrap X, Wrap Y", inspector.ModelInformation.SelectedMaterial.TextureSlots[0].Flags!.DisplayText);
        Assert.AreEqual("textures/dummy.blp", inspector.ModelInformation.SelectedMaterial!.Textures[0].Path);
        Assert.IsTrue(inspector.PlacementInformation.HasPlacement);
        Assert.AreEqual("42", inspector.PlacementInformation.Properties[0].Value);
        CollectionAssert.AreEqual(
            new[] { "Biodome", "Liquid Known" },
            inspector.PlacementInformation.Flags!.ActiveFlags.ToArray());

        inspector.Inspect(null);
        Assert.IsFalse(inspector.HasSelection);
        Assert.IsFalse(inspector.ModelInformation.HasModel);
        Assert.AreEqual(0, inspector.Sections.Count);
    }

    [TestMethod]
    public void ModelInformation_WorldModelGroupsAreSelectable()
    {
        var model = new ModelInformationViewModel();
        var groups = new[]
        {
            new WorldModelGroupData(0, "Exterior", "Outside", 10, 2, 600, 300, 4, 0x20),
            new WorldModelGroupData(1, "Interior", "Hall", 11, 3, 900, 450, 8, 0x40)
        };

        model.SetModel(new EditorObjectSnapshot(
            EditorObjectId.New(), "Keep", "World model", ObjectTransform.Identity,
            new WorldModelObjectData(
                1, 2, 2, 1, 5, true, "world/wmo/keep.wmo", [], 77,
                "world/maps/test/test_1_2.adt", groups,
                new MapPlacementData(MapPlacementKind.Modf, 77, 0x1, 1, 2),
                new WorldModelRootData(0xFF102030, 0x11),
                [new ModelMaterialData(0, 4, 5, "Diffuse", "MapObjDiffuse_T1", "MapObjDiffuse", [new AssetReference(9, "stone.blp")])],
                [new ModelBatchData(0, 1, 0, 3, 60, 4, 0, "Diffuse", "MapObjDiffuse_T1", "MapObjDiffuse", [new AssetReference(9, "stone.blp")])],
                ["Default", "Winter"])));

        Assert.IsTrue(model.HasWorldModelGroups);
        Assert.AreEqual("WMO groups (2)", model.WorldModelGroupsHeader);
        Assert.AreEqual("Textures (0)", model.TexturesHeader);
        Assert.AreEqual("Materials (1)", model.MaterialsHeader);
        Assert.AreEqual("Render batches (1)", model.BatchesHeader);
        Assert.AreEqual("Exterior", model.SelectedGroup!.Name);
        model.SelectedGroup = groups[1];
        Assert.AreEqual("Hall", model.SelectedGroupProperties[0].Value);
        Assert.AreEqual("MOGI name", model.SelectedGroupProperties[0].Label);
        Assert.AreEqual("3", model.SelectedGroupProperties[2].Value);
        Assert.AreEqual("Exterior Lit", model.SelectedGroupFlags!.DisplayText);
        Assert.AreEqual(0xFF102030u, model.RootAmbientColor);
        Assert.IsFalse(model.Properties.Any(property => property.Label == "Asset state"));
        Assert.IsNotNull(model.RootFlags);
        Assert.AreEqual("Additive (4)", model.SelectedMaterial!.Properties[0].Value);
        Assert.AreEqual(ModelMaterialType.Wmo, model.SelectedMaterial.MaterialType);
        Assert.IsTrue(model.HasBatches);
        Assert.AreEqual("Batch 0 · Group 1", model.SelectedBatch!.Name);
        Assert.AreSame(model.SelectedMaterial, model.SelectedBatch.Material);
        Assert.AreEqual(5, model.SelectedBatch.Properties.Count);
        Assert.AreEqual("stone.blp", model.SelectedMaterial.TextureSlots![0].File.Path);
    }

    [TestMethod]
    public void PlacementInformation_UsesModsNamesAndPublishesModfSetSelection()
    {
        var inspector = new SelectionInspectorViewModel([]);
        WmoPlacementSelection? changed = null;
        inspector.WmoPlacementChanged += (_, selection) => changed = selection;
        inspector.Inspect(new EditorObjectSnapshot(
            EditorObjectId.New(), "Keep", "World model", ObjectTransform.Identity,
            new WorldModelObjectData(
                1, 2, 1, 2, 4, true, "keep.wmo", [], 77, "map.adt", [],
                new MapPlacementData(MapPlacementKind.Modf, 77, 0, 1, 2),
                new WorldModelRootData(0xFF102030, 0), null, null,
                ["Set_$DefaultGlobal", "Set_Winter"])));

        var placement = inspector.PlacementInformation;
        Assert.IsTrue(placement.HasWorldModelPlacement);
        Assert.AreEqual("(1) Set_Winter", placement.SelectedDoodadSet!.DisplayName);
        Assert.AreEqual("2", placement.Properties.Single(property => property.Label == "Name set").Value);
        Assert.IsFalse(placement.Properties.Any(property => property.Label == "Active doodad sets"));

        placement.SelectedDoodadSet = placement.DoodadSetOptions[0];
        Assert.IsNotNull(changed);
        Assert.AreEqual((ushort)0, changed.DoodadSet);
        Assert.AreEqual((ushort)2, changed.NameSet);
    }

    [TestMethod]
    public void LoadedWmoProjection_PopulatesModsComboGroupsAndMaterialData()
    {
        var rendererModel = new WoWRenderLib.DX11.Structs.WorldModel
        {
            rootWMOFileDataID = 123,
            ambientColor = 0xFF102030,
            flags = 1,
            doodadSets = ["Set_$DefaultGlobal", "Set_Day"],
            doodads = [new WMODoodad { filedataid = 456, doodadSet = 1 }],
            preppedMats =
            [
                new PreppedWMOMaterial
                {
                    Shader = 0,
                    BlendMode = 0,
                    Color1 = 0xFF112233,
                    Color2 = 0xFF445566,
                    GroundType = 7,
                    TexFileDataID0 = 1001,
                    TexFileDataID1 = 1002,
                    TexFileDataID2 = 1003,
                    VertexShader = WoWRenderLib.Renderer.ShaderEnums.WMOVertexShader.MapObjDiffuse_T1,
                    PixelShader = WoWRenderLib.Renderer.ShaderEnums.WMOPixelShader.MapObjDiffuse
                }
            ],
            wmoRenderBatches =
            [
                new WMORenderBatch
                {
                    groupID = 0,
                    materialIndex = 0,
                    shader = 0,
                    numFaces = 3,
                    materialFDIDs = []
                }
            ],
            groupBatches =
            [
                new WorldModelGroupBatches
                {
                    groupName = "Exterior",
                    mogiGroupName = "Outside",
                    groupID = 7,
                    verticeCount = 3,
                    doodadReferences = [0],
                    portalLinks = []
                }
            ]
        };

        var data = WorldModelSelectionDisplayDataFactory.CreateLoaded(
            rendererModel, 123, 999, 42, 0, 1, 0, 1);
        Assert.IsTrue(data.IsLoaded);
        Assert.AreEqual(2, data.DoodadSets!.Count);
        Assert.AreEqual("Exterior", data.Groups![0].Name);
        Assert.AreEqual("Diffuse", data.Materials![0].Shader);
        Assert.AreEqual(3, data.Materials[0].TextureSlots!.Count);
        Assert.AreEqual(0xFF112233u, data.Materials[0].Color1);
        Assert.AreEqual(7u, data.Materials[0].GroundType);

        var snapshot = new EditorObjectSnapshot(
            EditorObjectId.New(), "Test WMO", "World model", ObjectTransform.Identity, data);
        var placement = new PlacementInformationViewModel();
        placement.SetPlacement(snapshot);
        Assert.AreEqual(2, placement.DoodadSetOptions.Count);
        Assert.AreEqual("(1) Set_Day", placement.SelectedDoodadSet!.DisplayName);
        var placementView = new PlacementInformationView { DataContext = placement };
        placementView.Measure(new global::Avalonia.Size(400, 800));
        var comboBox = placementView.FindControl<ComboBox>("DoodadSetComboBox");
        Assert.IsNotNull(comboBox);
        Assert.AreEqual(2, comboBox.ItemCount);

        var model = new ModelInformationViewModel();
        model.SetModel(snapshot);
        Assert.IsTrue(model.HasWorldModelGroups);
        Assert.AreEqual("Exterior", model.SelectedGroup!.Name);
        Assert.IsTrue(model.HasBatches);
        Assert.AreSame(model.SelectedMaterial, model.SelectedBatch!.Material);
        Assert.AreEqual("WMO groups (1)", model.WorldModelGroupsHeader);
        Assert.AreEqual("Textures (3)", model.TexturesHeader);
        Assert.AreEqual("Materials (1)", model.MaterialsHeader);
        Assert.AreEqual("Render batches (1)", model.BatchesHeader);
        Assert.AreEqual(3, model.SelectedMaterial!.TextureSlots!.Count);
        Assert.AreEqual(4, model.SelectedMaterial!.Colors!.Count);
        var modelView = new ModelInformationView { DataContext = model };
        modelView.Measure(new global::Avalonia.Size(400, 1200));
        var groupsList = modelView.FindControl<ListBox>("WmoGroupsList");
        Assert.IsNotNull(groupsList);
        Assert.AreEqual(1, groupsList.ItemCount);
        Assert.IsTrue(groupsList.IsVisible);
        Assert.IsNotNull(modelView.FindControl<MaterialDetailsView>("SelectedMaterialDetails"));
        Assert.IsNotNull(modelView.FindControl<MaterialDetailsView>("SelectedBatchMaterialDetails"));
    }

    [TestMethod]
    public void WmoDoodadSelection_EnablesDefaultAndPlacementSetsAndRejectsInvalidFdids()
    {
        var enabled = WMOContainer.BuildEnabledDoodadSetMask(3, [1u]);
        CollectionAssert.AreEqual(new[] { true, true, false }, enabled);
        Assert.IsTrue(SceneManager.IsWmoDoodadSpawnable(
            new WMODoodad { filedataid = 456, doodadSet = 1 }, enabled));
        Assert.IsFalse(SceneManager.IsWmoDoodadSpawnable(
            new WMODoodad { filedataid = 0, doodadSet = 1 }, enabled));
        Assert.IsFalse(SceneManager.IsWmoDoodadSpawnable(
            new WMODoodad { filedataid = 456, doodadSet = 2 }, enabled));
    }

    [TestMethod]
    public void WmoLegacyDoodadNames_UseM2Fallback()
    {
        Assert.AreEqual("world\\a.m2", WMOLoader.GetLegacyDoodadModelPath("world\\a.mdx"));
        Assert.AreEqual("world\\longer.m2", WMOLoader.GetLegacyDoodadModelPath("world\\longer.MDX"));
        Assert.IsNull(WMOLoader.GetLegacyDoodadModelPath("world\\already.m2"));
    }

}
