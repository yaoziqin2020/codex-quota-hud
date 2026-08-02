using System.Collections.ObjectModel;
using CodexQuotaHud.Skins.Templates.FreeDecorationRing;

namespace CodexQuotaHud.Skins.Templates;

public sealed class SkinTemplateRegistry
{
    private readonly IReadOnlyDictionary<TemplateKey, ISkinTemplate> _templates;

    private SkinTemplateRegistry(IEnumerable<ISkinTemplate> templates)
    {
        var entries = templates.ToDictionary(
            template => new TemplateKey(template.TemplateId, template.SchemaVersion));
        _templates = new ReadOnlyDictionary<TemplateKey, ISkinTemplate>(entries);
        RegisteredKeys = Array.AsReadOnly(entries.Keys
            .OrderBy(key => key.TemplateId, StringComparer.Ordinal)
            .ThenBy(key => key.SchemaVersion)
            .Select(key => (key.TemplateId, key.SchemaVersion))
            .ToArray());
    }

    public IReadOnlyList<(string TemplateId, int SchemaVersion)> RegisteredKeys
    {
        get;
    }

    public static SkinTemplateRegistry CreateDefault() =>
        new([new FreeDecorationRingTemplate()]);

    public bool TryResolve(
        string templateId,
        int schemaVersion,
        out ISkinTemplate template)
    {
        if (string.IsNullOrEmpty(templateId))
        {
            template = null!;
            return false;
        }

        return _templates.TryGetValue(
            new TemplateKey(templateId, schemaVersion),
            out template!);
    }

    private readonly record struct TemplateKey(
        string TemplateId,
        int SchemaVersion);
}
