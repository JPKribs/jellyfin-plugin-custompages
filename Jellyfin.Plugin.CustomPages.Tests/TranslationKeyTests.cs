using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using JPKribs.Jellyfin.Base;
using Xunit;

namespace Jellyfin.Plugin.CustomPages.Tests;

/// <summary>
/// Checks that every key the dashboard tabs use has a string, from Localization/en-us.json or a declared Jellyfin string.
/// </summary>
public partial class TranslationKeyTests
{
    private static readonly System.Reflection.Assembly PluginAssembly = typeof(Plugin).Assembly;

    public static TheoryData<string> WebFiles()
    {
        var data = new TheoryData<string>();
        foreach (var name in WebResources())
        {
            data.Add(name);
        }

        return data;
    }

    private static IEnumerable<string> WebResources()
        => PluginAssembly.GetManifestResourceNames()
            .Where(n => n.Contains(".Web.", System.StringComparison.Ordinal)
                && (n.EndsWith(".html", System.StringComparison.Ordinal) || n.EndsWith(".js", System.StringComparison.Ordinal)));

    [Theory]
    [MemberData(nameof(WebFiles))]
    public void EveryKeyAWebFileUses_HasAString(string resource)
    {
        var strings = BaseTranslations.For("en-us");
        var missing = KeysIn(Read(resource)).Where(k => !strings.ContainsKey(k)).ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void TheTabsUseKeys()
    {
        var keys = WebResources().SelectMany(r => KeysIn(Read(r))).ToHashSet();

        Assert.Contains("TabPages", keys);
        Assert.Contains("ConfirmClearStoreOne", keys);
        Assert.Contains("ConfirmClearStoreMany", keys);
        Assert.Contains("HeaderCustomPages", keys);
    }

    private static string Read(string resource)
    {
        using var stream = PluginAssembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static IEnumerable<string> KeysIn(string text)
    {
        foreach (Match m in MarkupKey().Matches(text))
        {
            yield return m.Groups[1].Value;
        }

        foreach (Match m in ScriptKey().Matches(text))
        {
            yield return m.Groups[1].Value;
            if (m.Groups[2].Success)
            {
                yield return m.Groups[2].Value;
            }
        }
    }

    // data-str, data-str-title, data-str-label, and data-str-placeholder.
    [GeneratedRegex("data-str(?:-title|-label|-placeholder)?=\"([^\"]+)\"")]
    private static partial Regex MarkupKey();

    // tr('Key') and format('Key', and a choice between two keys such as format(n === 1 ? 'One' : 'Many'.
    [GeneratedRegex(@"\b(?:tr|format)\((?:[^'()]*\?\s*)?'([A-Za-z]+)'(?:\s*:\s*'([A-Za-z]+)')?")]
    private static partial Regex ScriptKey();
}
