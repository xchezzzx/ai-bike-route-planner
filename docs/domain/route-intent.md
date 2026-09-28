# Route planning domain

This records the RouteIntent model agreed in issue #4 and the accompanying
learning session. RouteIntent is a normalized, validated planning request.
It does not generate a route or guarantee that a feasible route exists.

## Types and invariants

All types live in CyclingRoutes.Domain.RoutePlanning. The domain has no
dependencies on HTTP, AI, persistence, localization, or routing providers.

| Type | Properties | Rules |
| --- | --- | --- |
| GeoCoordinate | double Latitude, double Longitude | Finite degrees, latitude [-90, 90], longitude [-180, 180], inclusive |
| Distance | double Meters | Finite and strictly positive |
| CyclingProfile | Road = 1, Gravel = 2 | Only declared enum values |
| RouteShape | Loop = 1, PointToPoint = 2 | Only declared enum values |
| ElevationPreference | Minimize = 1, Balanced = 2, SeekClimbs = 3 | Only declared enum values |

GeoCoordinate and Distance are sealed records with explicit validating
constructors and get-only properties. No public setters or init accessors:
neither object initializers nor a with expression may bypass validation.
Equality compares stored values exactly. Coordinates are not rounded or
normalized; longitude -180 and 180 remain distinct representations.
Geographic proximity, road snapping, and equivalent physical positions are
future routing concerns, not floating-point tolerances in value equality.

RouteIntent is a sealed class with get-only properties:

| Property | Type | Rules |
| --- | --- | --- |
| Start | GeoCoordinate | Required, including runtime null validation |
| Shape | RouteShape | Defined value |
| Profile | CyclingProfile | Defined value |
| Elevation | ElevationPreference | Defined value; Balanced by default |
| Destination | GeoCoordinate? | Absent for Loop; required for PointToPoint and unequal to Start by value |
| TargetDistance | Distance? | Optional if TargetDuration is supplied |
| TargetDuration | TimeSpan? | Strictly positive when supplied |

At least one of TargetDistance and TargetDuration is required. Both may be
provided. They are preferences for candidate ranking, not guaranteed output
length/time or a calculated rider speed. Conflicting preferences will be
handled by the application and ranking stages.

Constructor order: start, shape, profile, targetDistance = null,
targetDuration = null, destination = null, elevation = Balanced.
Call sites should use named arguments for optional preferences.

Invalid individual values throw ArgumentOutOfRangeException; a null Start
throws ArgumentNullException; inconsistent combinations throw ArgumentException.
Exceptions identify the offending parameter when one can be identified.
HTTP error mapping will belong to the API layer.

## Product boundaries

Israel is the initial service area, not a restriction on GeoCoordinate.
Region eligibility, access restrictions, safety, and routing data quality
will require separate policies and provider validation. RU/EN/HE belong
to input/output presentation; this model contains no translated strings.
Raw prompts, extracted-but-incomplete AI data, confidence, explanations,
and vendor request formats remain outside the domain model.

## Verification

Unit tests exercise limits and non-finite numbers, value equality, valid
profile/shape/preference combinations, missing targets, invalid durations,
null start, destination rules, and undefined enum values. The existing
/health integration test remains a regression check for the host.
