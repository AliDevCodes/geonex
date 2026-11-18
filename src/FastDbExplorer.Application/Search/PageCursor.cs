namespace FastDbExplorer.Application.Search;

/// <summary>Where a page starts: an OFFSET, plus the key to continue after when keyset paging is in use.</summary>
public sealed record PageCursor(long Offset, object?[]? AfterKey);
