namespace Backend.DTOs
{
    public class KeylineCoordinateDto
    {
        public int? CoordinateID { get; set; }
        public string? ContentType { get; set; }
        public string? Grain { get; set; }
        public string? UpsType { get; set; }
        public string? ShapeType { get; set; }
        public string? ShapeName { get; set; }
        public string? LineType { get; set; }
        public string? AddInX1 { get; set; }
        public string? AddInY1 { get; set; }
        public string? AddInX2 { get; set; }
        public string? AddInY2 { get; set; }
        public string? AddInXForUps { get; set; }
        public string? AddInYForUps { get; set; }
        public string? LineStyles { get; set; }
        public string? SheetSize { get; set; }
    }

    public class KeylineFormulaDto
    {
        public int ID { get; set; }
        public string? Formula { get; set; }
    }

    public class KeylinePlanningDto
    {
        public int? FormulaID { get; set; }
        public string? ContentType { get; set; }
        public string? Grain { get; set; }
        public string? UpsType { get; set; }
        public string? SheetSize { get; set; }
        public string? Formula { get; set; }
    }

    public class SaveCoordinatesRequest
    {
        public List<KeylineCoordinateDto> Coordinates { get; set; } = new();
        public string ContentName { get; set; } = "";
        public string Grain { get; set; } = "";
        public string UpsType { get; set; } = "";
    }

    public class SavePlanningRequest
    {
        public List<KeylinePlanningDto> Planning { get; set; } = new();
        public string ContentName { get; set; } = "";
    }

    public class SaveFormulaRequest
    {
        public string Formula { get; set; } = "";
        public bool EditFlag { get; set; }
        public int? FormulaID { get; set; }
    }

    public class KeylineMetaDto
    {
        public IEnumerable<string> ShapeNames { get; set; } = [];
        public IEnumerable<string> FormulaX1 { get; set; } = [];
        public IEnumerable<string> FormulaY1 { get; set; } = [];
        public IEnumerable<string> FormulaX2 { get; set; } = [];
        public IEnumerable<string> FormulaY2 { get; set; } = [];
    }
}
