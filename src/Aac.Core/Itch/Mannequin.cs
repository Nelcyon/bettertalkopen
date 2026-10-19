namespace Aac.Core.Itch;

/// <summary>A point in the mannequin's own space: metres, y up, the figure facing +z, the user's left at +x.</summary>
public readonly record struct Vec(double X, double Y, double Z)
{
    public static Vec operator +(Vec a, Vec b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec operator -(Vec a, Vec b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec operator *(Vec a, double k) => new(a.X * k, a.Y * k, a.Z * k);
    public double Dot(Vec b) => X * b.X + Y * b.Y + Z * b.Z;
    public double Length => Math.Sqrt(Dot(this));
}

public enum PartShape
{
    /// <summary>A rounded tube from <see cref="BodyPart.A"/> to <see cref="BodyPart.B"/>.</summary>
    Capsule,
    /// <summary>An ellipsoid centred on <see cref="BodyPart.A"/> with radii <see cref="BodyPart.Radii"/>.</summary>
    Ellipsoid,
}

/// <summary>
/// One region of the body. <see cref="Name"/> is how it is said ("upper arm"); a limb runs from
/// <see cref="A"/> to <see cref="B"/>.
/// </summary>
public sealed record BodyPart(string Id, string Name, PartShape Shape, Vec A, Vec B, Vec Radii, double Radius, bool Paired = false)
{
    /// <summary>The id without its side ("thigh" for "thigh-l").</summary>
    public string Kind => Paired ? Id[..Id.LastIndexOf('-')] : Id;
}

/// <summary>
/// The figure's regions as simple shapes, fitted to the realistic body model the app draws (Blender
/// Studio's CC0 base mesh: 1.75 m, arms angled out, palms turned in toward the legs). A click on the model
/// is turned back into words by finding the nearest region with <see cref="Nearest"/>.
/// </summary>
public static class Mannequin
{
    public static readonly IReadOnlyList<BodyPart> Parts = Build();

    private static List<BodyPart> Build()
    {
        var parts = new List<BodyPart>
        {
            Ellipsoid("head", "head", new(0, 1.635, 0.01), new(0.092, 0.115, 0.11)),
            Ellipsoid("nose", "nose", new(0, 1.602, 0.118), new(0.017, 0.022, 0.02)),
            Capsule("neck", "neck", new(0, 1.46, -0.025), new(0, 1.55, -0.005), 0.062),
            Ellipsoid("chest", "chest", new(0, 1.3, -0.02), new(0.2, 0.16, 0.13)),
            Ellipsoid("belly", "stomach", new(0, 1.07, 0), new(0.16, 0.12, 0.11)),
            Ellipsoid("hips", "hips", new(0, 0.9, -0.025), new(0.18, 0.1, 0.12)),
        };
        foreach (var side in new[] { 1, -1 })
        {
            Vec S(double x, double y, double z) => new(side * x, y, z);
            var sfx = side > 0 ? "l" : "r";
            parts.Add(Ellipsoid($"ear-{sfx}", "ear", S(0.088, 1.625, 0.003), new(0.012, 0.035, 0.022), true));
            parts.Add(Ellipsoid($"shoulder-{sfx}", "shoulder", S(0.205, 1.41, -0.03), new(0.06, 0.055, 0.06), true));
            parts.Add(Capsule($"upperarm-{sfx}", "upper arm", S(0.235, 1.37, -0.04), S(0.3, 1.12, -0.03), 0.045, true));
            parts.Add(Capsule($"forearm-{sfx}", "forearm", S(0.3, 1.12, -0.03), S(0.385, 0.93, 0.03), 0.037, true));
            parts.Add(Ellipsoid($"hand-{sfx}", "hand", S(0.408, 0.865, 0.07), new(0.035, 0.045, 0.055), true));
            parts.Add(Capsule($"thumb-{sfx}", "thumb", S(0.386, 0.879, 0.109), S(0.381, 0.845, 0.138), 0.011, true));
            parts.Add(Capsule($"index-{sfx}", "index finger", S(0.433, 0.835, 0.122), S(0.433, 0.781, 0.133), 0.009, true));
            parts.Add(Capsule($"middle-{sfx}", "middle finger", S(0.443, 0.835, 0.088), S(0.447, 0.757, 0.11), 0.009, true));
            parts.Add(Capsule($"ring-{sfx}", "ring finger", S(0.441, 0.835, 0.06), S(0.443, 0.752, 0.079), 0.009, true));
            parts.Add(Capsule($"pinky-{sfx}", "pinky", S(0.431, 0.832, 0.03), S(0.44, 0.761, 0.03), 0.008, true));
            parts.Add(Capsule($"thigh-{sfx}", "thigh", S(0.11, 0.86, -0.02), S(0.14, 0.49, -0.045), 0.075, true));
            parts.Add(Capsule($"shin-{sfx}", "lower leg", S(0.14, 0.49, -0.045), S(0.178, 0.1, -0.09), 0.05, true));
            parts.Add(Ellipsoid($"foot-{sfx}", "foot", S(0.21, 0.04, -0.02), new(0.06, 0.045, 0.12), true));
        }
        return parts;
    }

    private static BodyPart Ellipsoid(string id, string name, Vec centre, Vec radii, bool paired = false) =>
        new(id, name, PartShape.Ellipsoid, centre, centre, radii, 0, paired);

    private static BodyPart Capsule(string id, string name, Vec a, Vec b, double r, bool paired = false) =>
        new(id, name, PartShape.Capsule, a, b, new(r, r, r), r, paired);

    public static BodyPart? Find(string id) => Parts.FirstOrDefault(p => p.Id == id);

    /// <summary>The region a point on the body's surface belongs to: the one whose shape it is closest to.</summary>
    public static BodyPart Nearest(Vec p) => Parts.MinBy(part => Distance(part, p))!;

    /// <summary>Roughly how far a point is outside (positive) or inside (negative) a part's shape, in metres.</summary>
    public static double Distance(BodyPart part, Vec p)
    {
        if (part.Shape == PartShape.Ellipsoid)
        {
            var d = p - part.A;
            var k = new Vec(d.X / part.Radii.X, d.Y / part.Radii.Y, d.Z / part.Radii.Z).Length;
            return k < 1e-9 ? -part.Radii.X : d.Length * (1 - 1 / k);
        }
        return (p - AxisPoint(part, p)).Length - part.Radius;
    }

    /// <summary>
    /// The point itself if it is within <paramref name="reach"/> of the body; otherwise the closest point
    /// that is (pulled straight back toward the nearest region).
    /// </summary>
    public static Vec Within(Vec p, double reach)
    {
        var part = Nearest(p);
        var gap = Distance(part, p) - reach;
        if (gap <= 0) return p;
        var centre = part.Shape == PartShape.Capsule ? AxisPoint(part, p) : part.A;
        var away = p - centre;
        var length = away.Length;
        return length < 1e-9 ? p : centre + away * ((length - gap) / length);
    }

    /// <summary>How far along a limb a point is: 0 at <see cref="BodyPart.A"/>, 1 at <see cref="BodyPart.B"/>.</summary>
    public static double Along(BodyPart part, Vec p)
    {
        var axis = part.B - part.A;
        return Math.Clamp((p - part.A).Dot(axis) / axis.Dot(axis), 0, 1);
    }

    private static Vec AxisPoint(BodyPart part, Vec p) => part.A + (part.B - part.A) * Along(part, p);

    /// <summary>Which way the surface at a point faces, seen from the limb's middle: front, back, outer or inner.</summary>
    public static Facing FacingOf(BodyPart part, Vec p)
    {
        var u = p - (part.Shape == PartShape.Capsule ? AxisPoint(part, p) : part.A);
        var outward = p.X >= 0 ? u.X : -u.X;
        if (Math.Abs(u.Z) >= Math.Abs(outward)) return u.Z >= 0 ? Facing.Front : Facing.Back;
        return outward >= 0 ? Facing.Outer : Facing.Inner;
    }
}

public enum Facing { Front, Back, Outer, Inner }

/// <summary>
/// Turns a picked point on the mannequin into the words a carer needs: which side (always the user's own), the exact
/// place (inner thigh, behind the ear, ring finger), and whether it is near a joint.
/// </summary>
public static class ItchDescriber
{
    /// <summary>"I have an itch on the back of my left upper arm, near the elbow."</summary>
    public static string Sentence(BodyPart part, Vec p) => $"I have an itch {Where(part, p)}.";

    /// <summary>Where it is, with its preposition: "on my left inner thigh", "behind my right ear".</summary>
    public static string Where(BodyPart part, Vec p)
    {
        var side = p.X >= 0 ? "left" : "right"; // the user's left is +x
        var x = Math.Abs(p.X);
        var t = part.Shape == PartShape.Capsule ? Mannequin.Along(part, p) : 0.5;
        var facing = Mannequin.FacingOf(part, p);

        switch (part.Kind)
        {
            case "head": return Head(p, side, x);
            case "nose": return "on my nose";
            case "ear": return $"on my {side} ear";
            case "neck":
                return facing switch
                {
                    Facing.Front => p.Y < 1.48 ? "on the bottom of my throat" : "on my throat",
                    Facing.Back => "on the back of my neck",
                    _ => $"on the {side} side of my neck",
                };
            case "chest": return Chest(p, side, x);
            case "belly": return Belly(p, side, x);
            case "hips": return Hips(p, side, x);
            case "shoulder":
                if (p.Y > part.A.Y + 0.025) return $"on the top of my {side} shoulder";
                if (p.Z > part.A.Z + 0.02) return $"on the front of my {side} shoulder";
                if (p.Z < part.A.Z - 0.02) return $"on the back of my {side} shoulder";
                return $"on the outside of my {side} shoulder";
            case "upperarm":
                if (t < 0.3 && facing == Facing.Inner) return $"in my {side} armpit";
                if (t > 0.82) return Elbow(facing, side);
                return facing switch
                {
                    Facing.Front => $"on the front of my {side} upper arm",
                    Facing.Back => $"on the back of my {side} upper arm",
                    Facing.Outer => $"on the outside of my {side} upper arm",
                    _ => $"on the inside of my {side} upper arm",
                } + (t < 0.3 ? ", near the shoulder" : t > 0.6 ? ", near the elbow" : ", in the middle");
            case "forearm":
                if (t < 0.15) return Elbow(facing, side);
                if (t > 0.82)
                {
                    return facing switch
                    {
                        Facing.Inner => $"on the inside of my {side} wrist",
                        Facing.Outer => $"on the back of my {side} wrist",
                        Facing.Front => $"on my {side} wrist, on the thumb side",
                        _ => $"on my {side} wrist, on the pinky side",
                    };
                }
                return facing switch
                {
                    Facing.Inner => $"on the inside of my {side} forearm",
                    Facing.Outer => $"on the back of my {side} forearm",
                    Facing.Front => $"on my {side} forearm, on the thumb side",
                    _ => $"on my {side} forearm, on the pinky side",
                } + (t < 0.4 ? ", near the elbow" : t > 0.65 ? ", near the wrist" : ", in the middle");
            case "hand":
                // The palms face in, toward the user's legs.
                if (x < part.A.X) return $"on the palm of my {side} hand";
                return p.Y < 0.84 ? $"on the knuckles of my {side} hand" : $"on the back of my {side} hand";
            case "thumb" or "index" or "middle" or "ring" or "pinky":
                return t > 0.7 ? $"on the tip of my {side} {part.Name}" : $"on my {side} {part.Name}";
            case "thigh":
                if (t > 0.86) return Knee(facing, side);
                return facing switch
                {
                    Facing.Front => $"on the front of my {side} thigh",
                    Facing.Back => $"on the back of my {side} thigh",
                    Facing.Outer => $"on the outside of my {side} thigh",
                    _ => $"on my {side} inner thigh",
                } + (t < 0.3 ? ", up high" : t > 0.62 ? ", just above the knee" : ", in the middle");
            case "shin":
                if (t < 0.1) return Knee(facing, side);
                if (t > 0.86)
                {
                    return facing switch
                    {
                        Facing.Outer => $"on the outside of my {side} ankle",
                        Facing.Inner => $"on the inside of my {side} ankle",
                        Facing.Back => $"on the back of my {side} ankle, above the heel",
                        _ => $"on the front of my {side} ankle",
                    };
                }
                return facing switch
                {
                    Facing.Front => $"on my {side} shin",
                    Facing.Back => $"on my {side} calf",
                    Facing.Outer => $"on the outside of my {side} lower leg",
                    _ => $"on the inside of my {side} lower leg",
                } + (t < 0.35 ? ", just below the knee" : t > 0.65 ? ", near the ankle" : ", in the middle");
            case "foot": return Foot(p, side, x);
        }
        return part.Paired ? $"on my {side} {part.Name}" : $"on my {part.Name}";
    }

    private static string Elbow(Facing facing, string side) =>
        facing is Facing.Front or Facing.Inner ? $"on the inside of my {side} elbow" : $"on my {side} elbow";

    private static string Knee(Facing facing, string side) => facing switch
    {
        Facing.Front => $"on my {side} kneecap",
        Facing.Back => $"on the back of my {side} knee",
        Facing.Outer => $"on the outside of my {side} knee",
        _ => $"on the inside of my {side} knee",
    };

    // Landmarks on the face (metres): eyes at 1.635, nose tip 1.60, mouth 1.555-1.585, chin below 1.55.
    private static string Head(Vec p, string side, double x)
    {
        // Around the ear (the ear itself is its own region).
        if (x > 0.065 && Math.Abs(p.Y - 1.625) < 0.04)
        {
            if (p.Z < -0.012) return $"behind my {side} ear";
            if (p.Z <= 0.03) return $"on my {side} ear";
            if (p.Z < 0.07) return $"in front of my {side} ear";
        }
        if (x > 0.065 && p.Y >= 1.665 && p.Y < 1.71 && Math.Abs(p.Z) < 0.04) return $"above my {side} ear";
        if (p.Y > 1.715 || (p.Y > 1.69 && p.Z > -0.05 && p.Z < 0.06)) return "on the top of my head";
        if (p.Z < -0.03)
        {
            var low = p.Y < 1.585 ? ", just above my neck" : "";
            return x > 0.05 ? $"on the back of my head, on the {side}{low}" : $"on the back of my head{low}";
        }
        if (p.Z < 0.05)
        {
            if (p.Y < 1.585) return $"on my {side} jaw";
            return $"on the {side} side of my head";
        }

        // The face.
        if (p.Y >= 1.66) return x > 0.055 ? $"on my {side} temple" : x > 0.025 ? $"on my forehead, on the {side}" : "on my forehead";
        if (p.Y >= 1.62) return x < 0.015 ? "between my eyes" : x < 0.06 ? $"near my {side} eye" : $"on my {side} temple";
        if (p.Y >= 1.585) return x < 0.02 ? "on my nose" : x > 0.07 ? $"on my {side} jaw" : $"on my {side} cheek";
        if (p.Y >= 1.55)
        {
            if (x < 0.025) return p.Y > 1.576 ? "on my upper lip" : "on my lips";
            if (x < 0.045) return $"by the {side} corner of my mouth";
            return x > 0.065 ? $"on my {side} jaw" : $"on my {side} cheek";
        }
        if (p.Y < 1.52 && p.Z < 0.1) return "under my chin";
        return x < 0.04 ? "on my chin" : $"on my {side} jaw";
    }

    private static string Chest(Vec p, string side, double x)
    {
        // Angle around the chest: 0 at the side, +90 at the front, -90 at the back.
        var angle = Math.Atan2((p.Z + 0.02) / 0.13, x / 0.2) * 180 / Math.PI;
        if (Math.Abs(angle) < 35)
            return p.Y >= 1.385 ? (x < 0.15 ? $"on the top of my {side} shoulder, near my neck" : $"on the top of my {side} shoulder") : p.Y > 1.28 ? $"in my {side} armpit" : $"on my {side} side, on my ribs";
        if (angle > 0)
        {
            if (p.Y > 1.4) return x > 0.03 ? $"on my {side} collarbone" : "on my chest, just below my neck";
            if (x < 0.035) return p.Y < 1.21 ? "on my breastbone, at the bottom" : "on the middle of my chest";
            return p.Y > 1.22 ? $"on the {side} side of my chest" : $"on my {side} ribs, at the front";
        }
        if (p.Y > 1.42) return x < 0.05 ? "on the top of my back, below my neck" : $"on my upper back, near my {side} shoulder";
        if (x < 0.035) return p.Y > 1.22 ? "on my spine, between my shoulder blades" : "on my spine, in the middle of my back";
        return p.Y > 1.22 ? $"on my {side} shoulder blade" : $"on my back, on the {side}, below the shoulder blade";
    }

    private static string Belly(Vec p, string side, double x)
    {
        var angle = Math.Atan2(p.Z / 0.11, x / 0.16) * 180 / Math.PI;
        if (Math.Abs(angle) < 35) return $"on my {side} side, at my waist";
        if (angle > 0)
        {
            if (x < 0.02 && Math.Abs(p.Y - 1.035) < 0.02) return "on my belly button";
            if (x < 0.03) return p.Y > 1.035 ? "on my stomach, above my belly button" : "on my stomach, below my belly button";
            return $"on the {side} side of my stomach";
        }
        return x < 0.035 ? "on my spine, in my lower back" : $"on my lower back, on the {side}";
    }

    private static string Hips(Vec p, string side, double x)
    {
        var angle = Math.Atan2((p.Z + 0.025) / 0.12, x / 0.18) * 180 / Math.PI;
        if (Math.Abs(angle) < 35) return $"on my {side} hip";
        if (angle < 0) return x < 0.03 && p.Y > 0.9 ? "on my tailbone" : $"on my {side} buttock";
        if (p.Y < 0.88 && x < 0.08) return x < 0.03 ? "in my groin" : $"in my groin, on the {side}";
        return x < 0.03 ? "on my lower belly" : $"on my lower belly, on the {side}";
    }

    // The feet point a little outward; the toes are in front (z above 0.045), the big toe on the inside.
    private static string Foot(Vec p, string side, double x)
    {
        if (p.Z > 0.045)
        {
            if (p.Y < 0.012) return $"under the toes of my {side} foot";
            if (x < 0.232) return $"on my {side} big toe";
            return x > 0.262 ? $"on my {side} little toe" : $"on the middle toes of my {side} foot";
        }
        if (p.Z < -0.095) return $"on my {side} heel";
        if (p.Y < 0.012) return x < 0.19 ? $"on the arch of my {side} foot" : $"on the sole of my {side} foot";
        if (x > 0.245) return $"on the outside edge of my {side} foot";
        if (x < 0.175) return $"on the inside of my {side} foot";
        return $"on the top of my {side} foot";
    }
}
