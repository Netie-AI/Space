using System.Text.Json;
using NetieSpace.Helpers;

namespace NetieSpace.Services;

/// <summary>Persisted signature presets (typed / image) under %LocalAppData%\NetieSpace\signatures.</summary>
public sealed class SignatureLibrary
{
    private readonly string _dir;
    private readonly string _indexPath;

    public SignatureLibrary()
    {
        _dir = IoPath.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NetieSpace", "signatures");
        IoDirectory.CreateDirectory(_dir);
        _indexPath = IoPath.Combine(_dir, "index.json");
    }

    public IReadOnlyList<SignatureEntry> List()
    {
        try
        {
            if (!IoFile.Exists(_indexPath)) return [];
            var json = IoFile.ReadAllText(_indexPath);
            return JsonSerializer.Deserialize<List<SignatureEntry>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public SignatureEntry AddTyped(string name, string font, string pngPath)
    {
        var id = Guid.NewGuid().ToString("N")[..12];
        var dest = IoPath.Combine(_dir, $"{id}.png");
        IoFile.Copy(pngPath, dest, overwrite: true);
        var entry = new SignatureEntry
        {
            Id = id,
            Label = name,
            Font = font,
            Kind = "typed",
            ImagePath = dest,
            CreatedUtc = DateTime.UtcNow
        };
        Save(List().Concat([entry]).ToList());
        return entry;
    }

    public SignatureEntry AddImage(string label, string imagePath)
    {
        var id = Guid.NewGuid().ToString("N")[..12];
        var ext = IoPath.GetExtension(imagePath);
        if (string.IsNullOrWhiteSpace(ext)) ext = ".png";
        var dest = IoPath.Combine(_dir, id + ext);
        IoFile.Copy(imagePath, dest, overwrite: true);
        var entry = new SignatureEntry
        {
            Id = id,
            Label = string.IsNullOrWhiteSpace(label) ? IoPath.GetFileNameWithoutExtension(imagePath) : label,
            Kind = "image",
            ImagePath = dest,
            CreatedUtc = DateTime.UtcNow
        };
        Save(List().Concat([entry]).ToList());
        return entry;
    }

    public bool Delete(string id)
    {
        var list = List().ToList();
        var hit = list.FirstOrDefault(e => e.Id == id);
        if (hit is null) return false;
        list.RemoveAll(e => e.Id == id);
        try { if (!string.IsNullOrWhiteSpace(hit.ImagePath) && IoFile.Exists(hit.ImagePath)) IoFile.Delete(hit.ImagePath); }
        catch { /* ignore */ }
        Save(list);
        return true;
    }

    private void Save(List<SignatureEntry> entries) =>
        IoFile.WriteAllText(_indexPath, JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
}

public sealed class SignatureEntry
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string Kind { get; set; } = "typed";
    public string? Font { get; set; }
    public string? ImagePath { get; set; }
    public DateTime CreatedUtc { get; set; }
}
