using System.Text.Json;
using WriteUp.Models;

namespace WriteUp.Services;

/// <summary>Session-local deep snapshots. Images are retained on disk when steps are deleted.</summary>
public sealed class StepHistory
{
    private readonly List<string> _states = new();
    private int _index = -1;
    public bool CanUndo => _index > 0;
    public bool CanRedo => _index >= 0 && _index < _states.Count - 1;
    public void Reset(IEnumerable<Step> steps) { _states.Clear(); _index = -1; Record(steps); }
    public void Record(IEnumerable<Step> steps)
    {
        string state = JsonSerializer.Serialize(steps);
        if (_index >= 0 && state == _states[_index]) return;
        _states.RemoveRange(_index + 1, _states.Count - _index - 1);
        _states.Add(state);
        if (_states.Count > 200) _states.RemoveAt(0);
        _index = _states.Count - 1;
    }
    public List<Step>? Undo() => CanUndo ? Read(--_index) : null;
    public List<Step>? Redo() => CanRedo ? Read(++_index) : null;
    private List<Step> Read(int index) => JsonSerializer.Deserialize<List<Step>>(_states[index])!;
}
