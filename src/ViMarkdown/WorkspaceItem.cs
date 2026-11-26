namespace ViMarkdown;

public sealed record WorkspaceItem(long Id, long? NoteId, string Name, bool IsNote, string Body)
{
    public DateTimeOffset UpdatedAt { get; init; }
    public DateTimeOffset? DeletedAt { get; init; }
    public override string ToString() => Name;
}
