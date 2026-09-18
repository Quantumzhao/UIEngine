using System.ComponentModel;
using UIEngine.Core;
using UIEngine.Nodes;

namespace UIEngine.Legacy.Characterization.Tests;

public sealed class LegacyDashboardFixture
{
    private readonly CharacterizedModel _model;

    public LegacyDashboardFixture()
    {
        _model = new CharacterizedModel();
        LegacyEntryPoints.Model = _model;
        Dashboard.ImportEntryObjects(typeof(LegacyEntryPoints));

        Root = Dashboard.GetRootNode<ObjectNode>(nameof(LegacyEntryPoints.Model))
            ?? throw new InvalidOperationException("The characterized root was not discovered.");
    }

    public CharacterizedModel Model => _model;

    public ObjectNode Root { get; }

    public void Reset() => Model.Reset();

    public static class LegacyEntryPoints
    {
        [Visible(nameof(Model))]
        public static CharacterizedModel Model { get; set; } = null!;

        public static CharacterizedModel HiddenModel => Model;
    }

    public sealed class CharacterizedModel : INotifyPropertyChanged
    {
        private int _value;

        public CharacterizedModel()
        {
            Items = new List<string> { "alpha", "beta" };
            Reset();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        [Visible(nameof(Value))]
        public int Value
        {
            get => _value;
            set
            {
                if (_value == value)
                {
                    return;
                }

                _value = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
            }
        }

        [Visible(nameof(Items))]
        public List<string> Items { get; }

        public string HiddenValue { get; } = "hidden";

        [Visible(nameof(Add))]
        public int Add([ParamInfo(nameof(delta))] int delta) => Value + delta;

        public void Reset() => Value = 2;
    }
}
