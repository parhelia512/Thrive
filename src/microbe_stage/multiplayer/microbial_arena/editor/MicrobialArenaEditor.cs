using System;
using System.Collections.Generic;
using Godot;
using Newtonsoft.Json;

/// <summary>
///   Main class of the microbe arena editor
/// </summary>
[JsonObject(IsReference = true)]
[SceneLoadedClass("res://src/microbe_stage/multiplayer/microbial_arena/editor/MicrobialArenaEditor.tscn")]
[DeserializedCallbackTarget]
[UseThriveSerializer]
public class MicrobialArenaEditor : MultiplayerEditorBase<EditorAction, MicrobialArena>, ICellEditorData
{
    [Export]
    public NodePath CellEditorTabPath = null!;

    private CellEditorComponent cellEditorTab = null!;

    private MicrobeSpecies? editedSpecies;

    public override bool CanCancelAction => cellEditorTab.CanCancelAction;

    public override Species EditedBaseSpecies =>
        editedSpecies ?? throw new InvalidOperationException("species not initialized");

    public ICellProperties? EditedCellProperties =>
        editedSpecies ?? throw new InvalidOperationException("species not initialized");

    public Patch CurrentPatch => CurrentGame.GameWorld.Map.CurrentPatch!;

    protected override string MusicCategory => "MicrobeEditor";

    protected override MainGameState ReturnToState => MainGameState.Invalid;

    protected override string EditorLoadingMessage => TranslationServer.Translate("LOADING_MICROBE_EDITOR");

    protected override bool HasInProgressAction => CanCancelAction;

    public override void _ExitTree()
    {
    }

    public override bool CancelCurrentAction()
    {
        if (!cellEditorTab.Visible)
        {
            GD.PrintErr("No action to cancel");
            return false;
        }

        return cellEditorTab.CancelCurrentAction();
    }

    public void OnCurrentPatchUpdated(Patch patch)
    {
        cellEditorTab.CalculateOrganelleEffectivenessInPatch(patch);
        cellEditorTab.UpdatePatchDependentBalanceData();
        cellEditorTab.UpdateBackgroundImage(patch.BiomeTemplate);
    }

    public override int WhatWouldActionsCost(IEnumerable<EditorCombinableActionData> actions)
    {
        return history.WhatWouldActionsCost(actions);
    }

    protected override void OnEnterEditor()
    {
        if (ReturnToStage != null)
            ReturnToStage.LocalPlayerSpeciesReceived = NotifyLocalPlayerSpeciesReceived;

        base.OnEnterEditor();
    }

    protected override void OnEditorExitTransitionFinished()
    {
        GD.Print(GetType().Name, ": applying changes to edited Species");

        foreach (var editorComponent in GetAllEditorComponents())
        {
            GD.Print(editorComponent.GetType().Name, ": applying changes of component");
            editorComponent.OnFinishEditing();
        }

        QueueFree();
        ReturnToStage?.OnReturnFromEditor();
    }

    protected override void InitEditor(bool fresh)
    {
        base.InitEditor(fresh);

        cellEditorTab.SetProcess(false);
        cellEditorTab.SetBlockSignals(true);

        cellEditorTab.UpdateBackgroundImage(CurrentPatch.BiomeTemplate);

        SetEditorTab(EditorTab.CellEditor);
    }

    protected override void ApplyEditorTab()
    {
        cellEditorTab.Show();
        SetEditorObjectVisibility(true);
        cellEditorTab.UpdateCamera();
    }

    protected override IEnumerable<IEditorComponent> GetAllEditorComponents()
    {
        yield return cellEditorTab;
    }

    protected override void InitEditorGUI(bool fresh)
    {
        cellEditorTab.OnFinish = ForwardEditorComponentFinishRequest;
        cellEditorTab.Init(this, fresh);

        cellEditorTab.SetProcess(true);
        cellEditorTab.SetBlockSignals(false);
    }

    protected override void ResolveDerivedTypeNodeReferences()
    {
        cellEditorTab = GetNode<CellEditorComponent>(CellEditorTabPath);
    }

    protected override void SetupEditedSpecies()
    {
        MultiplayerWorld.Species.TryGetValue((uint)GetTree().GetNetworkUniqueId(), out Species species);
        editedSpecies = (MicrobeSpecies)species ?? throw new NullReferenceException("didn't find edited species");

        base.SetupEditedSpecies();
    }

    [DeserializedCallbackAllowed]
    private void NotifyLocalPlayerSpeciesReceived()
    {
        receivedSpeciesFromServer = true;
    }
}
