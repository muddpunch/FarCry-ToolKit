using System.Windows.Media;

namespace Dunia.Toolkit.Browser;

internal static class ResourceTypeIcon
{
    private static readonly Geometry FileIcon = Create("M3,1 H10 L13,4 V15 H3 Z M10,1 V4 H13");
    private static readonly Geometry MeshIcon = Create("M8,1 L14,4.5 V11.5 L8,15 L2,11.5 V4.5 Z M2,4.5 L8,8 L14,4.5 M8,8 V15");
    private static readonly Geometry TextureIcon = Create("M2,2 H14 V14 H2 Z M3.5,12 L7,8 L9,10 L11,7 L14,11 M5,5 A1,1 0 1 1 4.99,5");
    private static readonly Geometry MaterialIcon = Create("M8,1.5 A6.5,6.5 0 1 1 7.99,1.5 M8,1.5 V14.5 M1.5,8 H14.5");
    private static readonly Geometry AudioIcon = Create("M1,8 H3 L5,4 V12 L8,6 V10 L10,5 V11 L13,7 V9 H15");
    private static readonly Geometry UiIcon = Create("M1.5,2 H14.5 V14 H1.5 Z M1.5,5 H14.5 M5,5 V14");
    private static readonly Geometry TextIcon = Create("M6,4 L2,8 L6,12 M10,4 L14,8 L10,12 M9,2 L7,14");
    private static readonly Geometry WorldIcon = Create("M1.5,3 L5.5,1.5 L10.5,3 L14.5,1.5 V13 L10.5,14.5 L5.5,13 L1.5,14.5 Z M5.5,1.5 V13 M10.5,3 V14.5");
    private static readonly Geometry DataIcon = Create("M2,4 C2,1.5 14,1.5 14,4 C14,6.5 2,6.5 2,4 Z M2,4 V8 C2,10.5 14,10.5 14,8 V4 M2,8 V12 C2,14.5 14,14.5 14,12 V8");
    private static readonly Geometry CollisionIcon = Create("M8,1.5 L14,4 V8.5 C14,11.5 11.5,14 8,15 C4.5,14 2,11.5 2,8.5 V4 Z M5,8 L7,10 L11,6");

    public static string GetKind(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Resource";
        }

        string value = name.Split(" | ", StringSplitOptions.RemoveEmptyEntries)[0].ToLowerInvariant();
        if (value.EndsWith(".xbg", StringComparison.Ordinal) || value.EndsWith(".mesh", StringComparison.Ordinal)) return "Mesh";
        if (value.EndsWith(".hkx", StringComparison.Ordinal)) return "Collision";
        if (value.EndsWith(".xbt", StringComparison.Ordinal) || value.EndsWith(".dds", StringComparison.Ordinal) || value.EndsWith(".png", StringComparison.Ordinal) || value.EndsWith(".tga", StringComparison.Ordinal)) return "Texture";
        if (value.Contains(".material", StringComparison.Ordinal)) return "Material";
        if (value.EndsWith(".wem", StringComparison.Ordinal) || value.EndsWith(".bnk", StringComparison.Ordinal) || value.EndsWith(".wav", StringComparison.Ordinal) || value.EndsWith(".ogg", StringComparison.Ordinal)) return "Audio";
        if (value.EndsWith(".feu", StringComparison.Ordinal)) return "UI";
        if (value.EndsWith(".lua", StringComparison.Ordinal) || value.EndsWith(".xml", StringComparison.Ordinal) || value.EndsWith(".json", StringComparison.Ordinal) || value.EndsWith(".txt", StringComparison.Ordinal) || value.EndsWith(".cfg", StringComparison.Ordinal)) return "Text";
        if (value.EndsWith(".world", StringComparison.Ordinal) || value.Contains("\\worlds\\", StringComparison.Ordinal)) return "World";
        if (value.EndsWith(".fcb", StringComparison.Ordinal) || value.EndsWith(".bin", StringComparison.Ordinal)) return "Data";
        return "File";
    }

    public static Geometry For(string kind) => kind switch
    {
        "Mesh" => MeshIcon,
        "Texture" => TextureIcon,
        "Material" => MaterialIcon,
        "Audio" => AudioIcon,
        "UI" => UiIcon,
        "Text" => TextIcon,
        "World" => WorldIcon,
        "Data" => DataIcon,
        "Collision" => CollisionIcon,
        _ => FileIcon,
    };

    private static Geometry Create(string data)
    {
        Geometry geometry = Geometry.Parse(data);
        geometry.Freeze();
        return geometry;
    }
}
