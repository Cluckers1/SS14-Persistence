using Content.Shared.Examine;
using Content.Shared._Persistence14.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;

namespace Content.Shared._Persistence14.Weapons.Ranged.Systems;

/// <summary>
/// Worsens a gun's spread while it is not wielded, see <see cref="GunUnwieldedPenaltyComponent"/>.
/// </summary>
public sealed partial class GunUnwieldedPenaltySystem : EntitySystem
{
    [Dependency] private SharedGunSystem _gun = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GunUnwieldedPenaltyComponent, ItemWieldedEvent>(OnWielded);
        SubscribeLocalEvent<GunUnwieldedPenaltyComponent, ItemUnwieldedEvent>(OnUnwielded);
        SubscribeLocalEvent<GunUnwieldedPenaltyComponent, GunRefreshModifiersEvent>(OnRefreshModifiers);
        SubscribeLocalEvent<GunUnwieldedPenaltyComponent, ExaminedEvent>(OnExamine);
    }

    private void OnWielded(Entity<GunUnwieldedPenaltyComponent> ent, ref ItemWieldedEvent args)
    {
        _gun.RefreshModifiers(ent.Owner);
    }

    private void OnUnwielded(Entity<GunUnwieldedPenaltyComponent> ent, ref ItemUnwieldedEvent args)
    {
        _gun.RefreshModifiers(ent.Owner);
    }

    private void OnRefreshModifiers(Entity<GunUnwieldedPenaltyComponent> ent, ref GunRefreshModifiersEvent args)
    {
        if (TryComp(ent, out WieldableComponent? wield) && wield.Wielded)
            return;

        args.MinAngle *= ent.Comp.AngleMultiplier;
        args.MaxAngle *= ent.Comp.AngleMultiplier;
        args.AngleIncrease *= ent.Comp.AngleMultiplier;
        args.AngleDecay /= ent.Comp.AngleMultiplier;
    }

    private void OnExamine(Entity<GunUnwieldedPenaltyComponent> ent, ref ExaminedEvent args)
    {
        if (HasComp<GunRequiresWieldComponent>(ent) || MathHelper.CloseTo(ent.Comp.AngleMultiplier, 1f))
            return;

        if (ent.Comp.ExamineMessage != null)
            args.PushText(Loc.GetString(ent.Comp.ExamineMessage));
    }
}
