using Content.Server.Chat.Managers;
using Content.Server.Chat.Systems;
using Content.Server.CrewRecords.Systems;
using Content.Server.Database;
using Content.Server.Traits;
using Content.Shared._Persistence14.Codex;
using Content.Shared._Persistence14.Codex.Components;
using Content.Shared._Persistence14.Codex.Prototypes;
using Content.Shared._Persistence14.Rumors.Components;
using Content.Shared.Anomaly.Components;
using Content.Shared.Atmos.Rotting;
using Content.Shared.Body.Components;
using Content.Shared.Chat;
using Content.Shared.CrewAssignments;
using Content.Shared.CrewAssignments.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Electrocution;
using Content.Shared.EntityEffects.Effects.Body;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.GridControl.Components;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Medical;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Traits.Assorted;
using Robust.Server.Player;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace Content.Server._Persistence14.Codex.Systems;

public sealed partial class CodexSystem : EntitySystem
{
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private MobThresholdSystem _mobThreshold = default!;
    [Dependency] private ISharedPlayerManager _player = default!;
    [Dependency] private CrewMetaRecordsSystem _crewMetaRecords = default!;
    [Dependency] private IChatManager _chatManager = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private IPrototypeManager _protoMan = default!;
    [Dependency] private TraitSystem _trait = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedElectrocutionSystem _electrocution = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private SharedRottingSystem _rotting = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ActorComponent, SenseAnomalyEvent>(OnSenseAnomaly);
        SubscribeLocalEvent<CodexFragmentComponent, UseInHandEvent>(CodexFragmentAfterInteract);
        SubscribeLocalEvent<CodexExamineUnlockerComponent, ExaminedEvent>(OnUnlockerExamined);
        SubscribeLocalEvent<MiracleReviveComponent, MiracleReviveEvent>(OnMiracleRevive);
    }

    private void OnMiracleRevive(Entity<MiracleReviveComponent> ent, ref MiracleReviveEvent args)
    {
        EntityUid target = args.Target;
        EntityUid user = ent.Owner;
        if(TryRevive(ent, user, target)) args.Handled = true;
    }

    private bool TryRevive(Entity<MiracleReviveComponent> ent, EntityUid user, EntityUid target)
    {
   
        bool failedRevive = true;
        if (_rotting.IsRotten(target))
        {
            _chat.TrySendInGameICMessage(ent.Owner, Loc.GetString("defibrillator-rotten"), InGameICChatType.Speak, true);
        }
        else if (TryComp<UnrevivableComponent>(target, out var unrevivable))
        {
            _chat.TrySendInGameICMessage(ent.Owner, Loc.GetString(unrevivable.ReasonMessage), InGameICChatType.Speak, true);
        }
        else
        {
            TryComp<MobStateComponent>(target, out var targetMobState);
            if (targetMobState == null || targetMobState.CurrentState != MobState.Dead) return false;
            _damageable.TryChangeDamage(target, ent.Comp.ZapHeal, true, origin: user);
            var canRevive = true;
            
            if (canRevive && // funky
                TryComp<MobThresholdsComponent>(target, out var targetThresholds) && // funky
                _mobThreshold.TryGetThresholdForState(target, MobState.Dead, out var threshold, targetThresholds) &&
                _damageable.GetTotalDamage(target) < threshold) //is their current health above their death threshold
            {
                _mobState.ChangeMobState(target, MobState.Critical, targetMobState, user); //if so revive them
                failedRevive = false;
                _electrocution.TryDoElectrocution(
                    target,
                    ent.Owner,
                    ent.Comp.ZapDamage,
                    ent.Comp.WritheDuration,
                    true
                );
                _audio.PlayPredicted(ent.Comp.ZapSound, ent, null);
            }
        }


        return !failedRevive;
    }

    public float? GetRange(EntityUid start, EntityUid destination)
    {
        var startXform = Transform(start);
        var destinationXform = Transform(destination);
        if (startXform.MapID != destinationXform.MapID) return null;
        var dist = Vector2.Distance(startXform.MapPosition.Position, destinationXform.MapPosition.Position);
        return dist;
    }
    public override void Update(float frameTime)
    {
        var passiveUnlockerQuery = EntityQueryEnumerator<CodexPassiveUnlockerComponent>();
        while (passiveUnlockerQuery.MoveNext(out var uid, out var comp))
        {
            if(comp.EntryToAdd != "" && comp.ToAddRequirement != 0)
            {
                comp.PossibleEntries.Add(comp.EntryToAdd, comp.ToAddRequirement);
                comp.EntryToAdd = "";
                comp.ToAddRequirement = 0;
            }
            if (_timing.CurTime > comp.CurrentCooldown)
            {
                PassiveUnlockerCheck(uid, comp);
            }
        }
        base.Update(frameTime);
    }
    
    private void PassiveUnlockerCheck(EntityUid uid, CodexPassiveUnlockerComponent comp)
    {
        if (_crewMetaRecords.MetaRecords == null) return;
        comp.CurrentCooldown = _timing.CurTime + comp.CheckCooldown;
        var actorQuery = EntityQueryEnumerator<ActorComponent>();
        while (actorQuery.MoveNext(out var actorEnt, out var actorComp))
        {
            if (Transform(actorEnt).GridUid != uid) continue;
            if (!_crewMetaRecords.MetaRecords.TryGetRecord(Name(actorEnt), out var crewrecord) || crewrecord == null) continue;
            if (_random.Next(0, 2) == 0) continue;
            foreach(var kv in comp.PossibleEntries)
            {
                var entry = kv.Key;
                var progressRequired = kv.Value;
                if (crewrecord.UnlockedEntries.Contains(entry)) continue;
                if (crewrecord.PassiveProgressEntries.ContainsKey(entry))
                {
                    crewrecord.PassiveProgressEntries[entry]++;
                }
                else
                {
                    crewrecord.PassiveProgressEntries[entry] = 1;
                }
                var progress = crewrecord.PassiveProgressEntries[entry];
                if(progress >= progressRequired)
                {
                    crewrecord.PassiveProgressEntries.Remove(entry);
                    UnlockEntry(actorEnt, entry);
                }
                break;
            }
        }
    }

    private void OnUnlockerExamined(Entity<CodexExamineUnlockerComponent> ent, ref ExaminedEvent args)
    {
        if (ent.Comp.Entry == "") return;
        var mob = args.Examiner;
        if (_crewMetaRecords.MetaRecords == null) return;
        if (!_crewMetaRecords.MetaRecords.TryGetRecord(Name(mob), out var crewrecord) || crewrecord == null) return;
        if(!crewrecord.UnlockedEntries.Contains(ent.Comp.Entry))
        {
            UnlockEntry(mob, ent.Comp.Entry);
        }
    }

    private void CodexFragmentAfterInteract(Entity<CodexFragmentComponent> ent, ref UseInHandEvent args)
    {
        var mob = args.User;
        if (_crewMetaRecords.MetaRecords == null) return;
        if (!_crewMetaRecords.MetaRecords.TryGetRecord(Name(mob), out var crewrecord) || crewrecord == null) return;
        foreach (var entry in ent.Comp.PossibleEntries)
        {
            if (crewrecord.UnlockedEntries.Contains(entry)) continue;
            UnlockEntry(mob, entry);
            args.Handled = true;
            QueueDel(ent);
            return;
        }
        NotifyPlayer(mob, "You have learned everything this has to teach.");
    }

    private void OnSenseAnomaly(Entity<ActorComponent> ent, ref SenseAnomalyEvent args)
    {
        EntityUid? closest = null;
        float? distance = null;
        foreach(var arti in _lookup.GetEntitiesInRange<AnomalyComponent>(Transform(ent).Coordinates, 1000))
        {
            if (closest == null || distance == null)
            {
                distance = GetRange(ent, arti);
                closest = arti;
            }
            else
            {
                var range = GetRange(ent, arti);
                if (range != null && range < distance)
                {
                    closest = arti;
                    distance = range;
                }
            }
        }
        if(closest != null)
        {
            var active = Transform(closest.Value);
            NotifyPlayer(ent, $"You sense an anomaly; {Name(closest.Value)} is at {Math.Round(active.MapPosition.Position.X)}, {Math.Round(active.MapPosition.Position.Y)}");
        }
        else
        {
            NotifyPlayer(ent, "There are no anomalies within a 1000m range.");
        }
        args.Handled = true;
    }

    public void UnlockEntry(EntityUid mob, ProtoId<CodexEntryPrototype> codex)
    {
        if (_crewMetaRecords.MetaRecords == null) return;
        if (!_crewMetaRecords.MetaRecords.TryGetRecord(Name(mob), out var crewrecord) || crewrecord == null) return;
        crewrecord.UnlockedEntries.Add(codex);
        var codexProto = _protoMan.Index(codex);
        var categoryProto = _protoMan.Index(codexProto.Category);
        NotifyPlayer(mob, $"You have unlocked the codex entry '[bold]{codexProto.Name}[/bold]' in the {categoryProto.Name} series.");
        crewrecord.CodexUnlocksSince++;
        if(crewrecord.CodexUnlocksSince >= 5)
        {
            crewrecord.UnspentInsights++;
            crewrecord.CodexUnlocksSince = 0;
            NotifyPlayer(mob, $"You have enough codex entries to earn an Insight. Spend Insights in the Job Network.");
        }
    }

    public void NotifyPlayer(EntityUid player, string msg, SoundSpecifier? sound = null, EntityUid? source = null)
    {
        if (TryComp<ActorComponent>(player, out var actor) && actor != null && actor.PlayerSession != null)
        {
            _chatManager.ChatMessageToOne(Shared.Chat.ChatChannel.Notifications,
                msg,
                msg,
                player,
                false,
                actor.PlayerSession.Channel
                );
        }
        if (sound != null && source != null)
        {
            _audio.PlayEntity(sound, player, source.Value);
        }
    }

    public void OnInsightPurchase(EntityUid ent, JobNetInsightPurchaseMessage args)
    {
        if(_crewMetaRecords.MetaRecords == null) return;
        if (!_crewMetaRecords.MetaRecords.TryGetRecord(Name(ent), out var crewrecord) || crewrecord == null) return;
        if (crewrecord.UnspentInsights <= 0) return;
        if (crewrecord.UnlockedInsights.Contains(args.Target)) return;
        crewrecord.UnspentInsights--;
        crewrecord.UnlockedInsights.Add(args.Target);
        var insightProto = _protoMan.Index<InsightTraitPrototype>(args.Target);
        var traitProto = _protoMan.Index(insightProto.Trait);
        _trait.AddTrait(ent, traitProto);

    }
}
