using CommunityToolkit.Mvvm.ComponentModel;
using LifeSim.Core.Brains;
using LifeSim.Core.Configuration;

namespace LifeSim.App.ViewModels;

/// <summary>
/// One row of the founding-population composition: a brain "type", how many founders to seed with it,
/// and (for scripted types) its editable script with live parse validation. A generic row seeds the
/// evolved brain (no script); built-in rows are the shipped example scripts, editable in place; custom
/// rows are user-authored. Contributes a <see cref="BrainTypeSpec"/> only when its count is positive.
/// </summary>
public partial class BrainTypeRowViewModel : ObservableObject
{
    public BrainTypeRowViewModel(string name, string? script, int count, bool isGeneric, bool isRemovable, bool sexual = false)
    {
        _name = name;
        _scriptText = script ?? string.Empty;
        _count = count;
        _sexual = sexual;
        IsGeneric = isGeneric;
        IsRemovable = isRemovable;
        Validate();
    }

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _scriptText;

    /// <summary>Founder count for this type; decimal to bind a whole-number NumericUpDown.</summary>
    [ObservableProperty]
    private decimal _count;

    /// <summary>Seed this type's founders sexual (Sexuality = 1) rather than asexual; still evolves from there.</summary>
    [ObservableProperty]
    private bool _sexual;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    private string? _parseError;

    /// <summary>Generic (evolved) brain — no script; it always compiles.</summary>
    public bool IsGeneric { get; }

    /// <summary>Custom rows can be removed; the generic row and shipped examples cannot.</summary>
    public bool IsRemovable { get; }

    /// <summary>Scripted rows (everything but Generic) show and validate a script editor.</summary>
    public bool HasScript => !IsGeneric;

    public bool HasError => ParseError is not null;

    public bool IsValid => ParseError is null;

    partial void OnScriptTextChanged(string value) => Validate();

    /// <summary>The config spec for this row, or null when its count is zero (nothing to seed).</summary>
    public BrainTypeSpec? ToSpec()
    {
        int count = (int)Count;
        if (count <= 0)
        {
            return null;
        }

        return new BrainTypeSpec
        {
            Name = string.IsNullOrWhiteSpace(Name) ? "Custom" : Name.Trim(),
            Script = IsGeneric ? null : ScriptText,
            Count = count,
            Sexuality = Sexual ? 1.0 : 0.0,
        };
    }

    // Compile the script to surface any authoring error the same way genesis would — no PRNG, cheap,
    // safe to run on every keystroke. Generic rows have no script and are always valid.
    private void Validate()
    {
        if (IsGeneric)
        {
            ParseError = null;
            return;
        }

        try
        {
            BrainTemplateCompiler.Compile(BrainScriptParser.ParseTemplate(ScriptText));
            ParseError = null;
        }
        catch (BrainScriptException ex)
        {
            ParseError = ex.Message;
        }
    }
}
