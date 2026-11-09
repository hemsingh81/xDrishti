# 0246 — The settings catalog: one descriptor per key, five Essential settings, and an undeclared direction loosens both ways

- **Status:** Accepted by delegation (owner instruction in chat 2026-11-09: research best practice, choose the best, record it here). Built in AU-08.
- **Date:** 2026-11-09.
- **Relation to other ADRs:** Applies [0242](0242-few-visible-settings-and-learned-changes-tighten-automatically-while-loosening-needs-the-owners-password.md); feeds AU-09 (screen) and AU-10 (learned tightening).

## Context
ADR 0242 asks for a descriptor per setting. Several choices were left open: how to cover many options classes, what "safer" means for a key nobody classified, where defaults and ranges come from, and what the Essential five are.

## Decisions
1. **Registry.** `SettingDescriptor` (key, label, tier, kind, default, min, max, safer, learnable) lives in `Application/SettingsCatalog`. The `Confidence:*` keys are described one by one in the Api (a unit-style test fails when a key of `ConfidenceSettings` has no descriptor, and the default of each descriptor is read from a fresh `ConfidenceSettings`, so it cannot drift). Every other options section bound at start-up gets one Internal section descriptor in `SettingsRegistry`; an architecture test scans the source for `SectionName` constants and `BindConfiguration("...")` literals and fails on a section with no descriptor. Section descriptors show no value.
2. **Best practice used.** Defaults and validation stay where they are (typed options with start-up validation, as .NET recommends); the descriptor is metadata, not a second source of truth. Its bounds are the inclusive outer bounds of the existing validation messages, checked at start by `ConfidenceRangeValidator` as an extra `IValidateOptions`. Because they are outer bounds, the validator stops the start-up in no case the existing validators accepted, and a tier move changes no validation and no default.
3. **Safer direction.** `higher`, `lower` or `none`. Declared only where the validation says the key is tighten-only or a higher bar is plainly stricter; everything else is `none`, which `IsLoosening` treats as loosening in both directions (so AU-10 needs the password for any change to it). The direction of every declared key is for the independent reviewer to confirm key by key.
4. **Tiers.** Essential, exactly five at the start (limit eight, enforced by test): `auto.enabled` (Auto), `suggest.scope` (What to suggest, default `eligible`), `risk.profile` (Risk profile), `chargesheet.gate` (Charge sheet), `notices.failure` (Failure notices, default on). Advanced: the stored owner switches (all OFF) and the Confidence sections the owner may tighten (Readiness, Kill, MlInfluence, Decay, CostStress). Internal: all other keys and every non-Confidence section.
5. **Learnable** is false for every key; AU-10 allow-lists keys explicitly.
6. **Catalog.** `GET /api/settings/catalog` (authenticated) returns key, label, tier, value, default, range, safer, learnable, source. Only Confidence keys carry a value (`default` or `configuration`); stored switches and sections carry none (`stored`, `section`), so no secret can appear; a test scans key names for secret-looking words.

## Consequences
Five Essential descriptors name settings whose store is built by later slices (`auto.enabled` AU-17, `suggest.scope` AU-16, `notices.failure` AU-09); until then they show their default and `stored`. Moving a setting between tiers is a one-line change in the registry.
