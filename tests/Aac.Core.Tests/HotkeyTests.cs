using Aac.Core.Input;

namespace Aac.Core.Tests;

public class HotkeyTests
{
    [Theory]
    [InlineData("Ctrl+Shift+F12", HotkeyModifiers.Control | HotkeyModifiers.Shift, "F12")]
    [InlineData("alt + space", HotkeyModifiers.Alt, "space")]
    [InlineData("Win+Return", HotkeyModifiers.Win, "Enter")]
    [InlineData("F6", HotkeyModifiers.None, "F6")]
    public void Reads_modifiers_and_the_key(string spec, HotkeyModifiers modifiers, string key)
    {
        Assert.True(HotkeySpec.TryParse(spec, out var mods, out var k, out var problem), problem);
        Assert.Equal(modifiers, mods);
        Assert.Equal(key, k);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl+Shift")]
    [InlineData("A")]
    [InlineData("Space")]
    [InlineData("Ctrl+A+B")]
    public void Refuses_what_would_break_other_programs_or_cannot_be_read(string spec)
    {
        Assert.False(HotkeySpec.TryParse(spec, out _, out _, out var problem));
        Assert.False(string.IsNullOrWhiteSpace(problem));
    }

    [Fact]
    public void Formats_the_way_it_reads()
    {
        var spec = HotkeySpec.Format(HotkeyModifiers.Control | HotkeyModifiers.Win, "F3");
        Assert.Equal("Ctrl+Win+F3", spec);
        Assert.True(HotkeySpec.TryParse(spec, out var mods, out var key, out _));
        Assert.Equal(HotkeyModifiers.Control | HotkeyModifiers.Win, mods);
        Assert.Equal("F3", key);
    }

    [Fact]
    public void No_hotkey_is_set_by_default()
    {
        Assert.Empty(new Aac.Core.Storage.AppSettings().Hotkeys);
    }

    [Fact]
    public void Every_hotkey_action_and_every_assignable_key_action_has_a_label()
    {
        foreach (var action in Enum.GetNames<HotkeyAction>())
            Assert.Contains(ActionCatalog.Hotkeys, a => a.Name == action);
        foreach (var info in ActionCatalog.Keys)
            Assert.True(Enum.TryParse<InputAction>(info.Name, out _), info.Name);
        // Only the two kept for older settings files (Approve, MakeSentences) are missing from the list.
        var listed = ActionCatalog.Keys.Select(k => k.Name).ToHashSet();
        var unlisted = Enum.GetNames<InputAction>().Where(n => !listed.Contains(n)).ToList();
        Assert.Equal(new[] { nameof(InputAction.Approve), nameof(InputAction.MakeSentences) }, unlisted);
    }

    [Fact]
    public void A_key_can_be_given_to_Space_so_a_sip_or_a_chin_switch_types_the_space()
    {
        Assert.Contains(ActionCatalog.Keys, a => a.Name == nameof(InputAction.Space));
        var map = KeyMap.FromDictionary(new Dictionary<string, string> { ["F10"] = nameof(InputAction.Space) });
        Assert.Equal(InputAction.Space, map.Resolve("F10"));

        var rig = new Rig();
        rig.Type("hello");
        rig.Session.HandleAction(InputAction.Space);
        Assert.Equal("hello ", rig.Session.Draft);
        rig.Session.HandleAction(InputAction.Space); // no doubled spaces
        Assert.Equal("hello ", rig.Session.Draft);
        rig.Type("there");
        Assert.Equal("hello there", rig.Session.Draft);
    }

    [Fact]
    public void Default_keys_bind_only_listed_actions_and_each_key_once()
    {
        var map = KeyMap.Default();
        Assert.Equal(InputAction.DeleteWord, map.Resolve("Delete"));
        Assert.Equal(KeyMap.DefaultBindings.Count, map.Bindings.Count);
    }
}
