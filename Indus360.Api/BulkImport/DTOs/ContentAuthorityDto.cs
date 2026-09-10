namespace Backend.DTOs;

public class ContentAuthorityRowDto
{
    public string ContentName { get; set; } = string.Empty;
    public bool IsSelected { get; set; }      // true = exists in client DB AND IsActive = 1
    public bool ExistsInClientDb { get; set; } // content row exists in client DB (even if inactive)
    public string ContentCaption { get; set; } = string.Empty;
    public string ContentOpenHref { get; set; } = string.Empty;
    public string ContentClosedHref { get; set; } = string.Empty;
}

public class ContentAuthoritySaveRequest
{
    public List<string> SelectedContents { get; set; } = new();    // activate / insert
    public List<string> DeselectedContents { get; set; } = new();  // set IsActive = 0
}

public class ContentAuthoritySaveResult
{
    public int Processed { get; set; }
    public int Inserted { get; set; }
    public int Updated { get; set; }
    public int Deactivated { get; set; }
    public int ChildRowsDeleted { get; set; }
    public int ChildRowsInserted { get; set; }
    public string Message { get; set; } = string.Empty;
}
