using Lumina.Excel.Sheets;

namespace Market.Widget.Filters;
internal abstract class Filter
{
    internal float _nameWidth = 0;
    protected bool Modified;
    public abstract string Name { get; }
    public abstract bool IsSet { get; }
    public virtual bool CanBeHidden { get; set; } = true;

    /// <summary>
    /// If not set, will be shown above the table, SameLine'd with each other
    /// </summary>
    public virtual bool ShowName { get; set; } = true;
    public virtual bool HasChanged
    {
        get
        {
            if (!Modified) return false;
            Modified = false;
            return true;
        }
    }

    public virtual bool CheckFilter(Item item) => true;
    public abstract void Draw();
}
