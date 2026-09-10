using Backend.DTOs;

namespace Backend.Services
{
    public interface IKeylineService
    {
        Task<IEnumerable<string>> GetContentNamesAsync();
        Task<IEnumerable<string>> GetShapeNamesAsync(string contentType, string grain, string upsType);
        Task<IEnumerable<KeylineCoordinateDto>> GetCoordinatesAsync(string contentType, string grain, string upsType);
        Task<IEnumerable<KeylineCoordinateDto>> GetShapeWiseDataAsync(string contentType, string grain, string upsType, string shapeName);
        Task<IEnumerable<KeylineFormulaDto>> GetFormulasAsync();
        Task<IEnumerable<string>> GetFormulaValuesAsync(string axis, string contentType, string grain, string upsType);
        Task<KeylineMetaDto> GetMetaAsync(string contentType, string grain, string upsType);
        Task SaveCoordinatesAsync(SaveCoordinatesRequest request);
        Task SaveFormulaAsync(SaveFormulaRequest request);
        Task DeleteFormulaAsync(int formulaId);
        Task DeleteCoordinatesAsync(string contentName, string grain, string upsType);
        Task<IEnumerable<KeylinePlanningDto>> GetPlanningAsync(string contentType);
        Task SavePlanningAsync(SavePlanningRequest request);
        Task DeletePlanningAsync(string contentName);
    }
}
