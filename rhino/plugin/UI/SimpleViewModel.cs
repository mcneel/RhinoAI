using System.ComponentModel;

#nullable enable

namespace Rhino.AI;

internal class SimpleViewModel : INotifyPropertyChanged, IDisposable
{

  protected bool SetProperty<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string name = "")
  {
    if (Equals(field, value)) return false;
    field = value;
    RaisePropertyChanged(name);
    return true;
  }

  // TODO : It would be nice to queue these items to prevent too many updates
  public void RaisePropertyChanged(string name)
  {
    if (string.IsNullOrEmpty(name)) return;
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
  }

  public event PropertyChangedEventHandler? PropertyChanged;


  public bool IsDisposed { get; set; } = false;

  public virtual void Dispose()
  {
    IsDisposed = true;
  }

}
