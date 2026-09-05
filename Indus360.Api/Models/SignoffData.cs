namespace Indus360.Api.Models;

/// <summary>
/// Auto-fill values for the client Sign-Off document, gathered live when the user opens
/// Sign-Off. Sourced from the control DB (client master), the client's own product DB
/// (DB create date, contact person, in-scope modules) and the app DB (milestone Res. Person
/// = implementation engineer, and the sign-off revision → version). Any field the source can't
/// provide (e.g. a client DB that is unreachable from the current network) is returned blank
/// so the document still opens.
/// </summary>
public sealed class SignoffData
{
    public string DocumentCode { get; set; } = "";              // IA-ERP-CLS-{CompanyUniqueCode}
    public string Version { get; set; } = "1.0";                // 1.0, 1.1, 1.2 … (revision-based)
    public string DocumentDate { get; set; } = "";              // today (send date)
    public string ErpProduct { get; set; } = "";               // from the client's Application/product
    public string CompanyName { get; set; } = "";
    public string Address { get; set; } = "";
    public string City { get; set; } = "";
    public string ProjectStartDate { get; set; } = "";          // client DB create_date (dd-MMM-yyyy, display)
    public string ProjectStartDateIso { get; set; } = "";       // client DB create_date (yyyy-MM-dd, for the date picker)
    public string GoLiveDate { get; set; } = "";                // = DocumentDate (send date)
    public string ProjectCompletionDate { get; set; } = "";     // = DocumentDate (send date)
    public string ContactPerson { get; set; } = "";             // client CompanyMaster contact person
    public string ImplementationEngineer { get; set; } = "";    // milestone Res. Person (Indus)
    public string ImplementationEngineerMobile { get; set; } = ""; // that engineer's app.Users mobile
    public string ImplementationHead { get; set; } = "Mahesh Patidar";
    public string SupportEmail { get; set; } = "maheshpatidar.indusanalytics@gmail.com";
    public List<string> SupportEmails { get; set; } = new();     // §8 Support Email multi-select options: app.Users Role='Support', active
    public Dictionary<string, string> UserMobiles { get; set; } = new(); // lower(name|email) → mobile; lets §8 Support Contact follow §2 Implementation Engineer client-side
    public List<string> InScopeModules { get; set; } = new();   // module head-names present in the client DB
}
