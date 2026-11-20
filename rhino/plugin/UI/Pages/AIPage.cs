using Eto.Forms;

namespace Rhino.AI.UI;

internal abstract class AIPage : Panel
{

    public abstract bool Validate();

    public abstract bool TryApplyChanges();

}
