using Backend.DTOs;
using Dapper;
using System.Data;
using Microsoft.Data.SqlClient;

namespace Backend.Services
{
    public class KeylineService : IKeylineService
    {
        private readonly SqlConnection _connection;

        public KeylineService(SqlConnection connection)
        {
            _connection = connection;
        }

        public async Task<IEnumerable<string>> GetContentNamesAsync()
        {
            const string sql = "SELECT DISTINCT ContentName FROM ContentWiseKeylineContentName ORDER BY ContentName";
            return await _connection.QueryAsync<string>(sql);
        }

        public async Task<IEnumerable<string>> GetShapeNamesAsync(string contentType, string grain, string upsType)
        {
            const string sql = @"SELECT DISTINCT ShapeName FROM ContentWiseKeylineCoordinates
                                 WHERE ContentType = @ContentType AND Grain = @Grain AND UpsType = @UpsType
                                 ORDER BY ShapeName";
            return await _connection.QueryAsync<string>(sql, new { ContentType = contentType, Grain = grain, UpsType = upsType });
        }

        public async Task<IEnumerable<KeylineCoordinateDto>> GetCoordinatesAsync(string contentType, string grain, string upsType)
        {
            const string sql = @"SELECT CoordinateID, ContentType, Grain, UpsType,
                                    NULLIF(ShapeType,'') AS ShapeType,
                                    NULLIF(ShapeName,'') AS ShapeName,
                                    AddInX1, AddInY1, AddInX2, AddInY2,
                                    AddInXForUps, AddInYForUps,
                                    NULLIF(LineType,'') AS LineType,
                                    NULLIF(LineStyles,'') AS LineStyles,
                                    NULLIF(SheetSize,'') AS SheetSize
                                 FROM ContentWiseKeylineCoordinates
                                 WHERE ContentType = @ContentType AND Grain = @Grain AND UpsType = @UpsType
                                 ORDER BY CoordinateID";
            return await _connection.QueryAsync<KeylineCoordinateDto>(sql, new { ContentType = contentType, Grain = grain, UpsType = upsType });
        }

        public async Task<IEnumerable<KeylineCoordinateDto>> GetShapeWiseDataAsync(string contentType, string grain, string upsType, string shapeName)
        {
            const string sql = @"SELECT CoordinateID, ContentType, Grain, UpsType,
                                    NULLIF(ShapeType,'') AS ShapeType,
                                    NULLIF(ShapeName,'') AS ShapeName,
                                    AddInX1, AddInY1, AddInX2, AddInY2,
                                    AddInXForUps, AddInYForUps,
                                    NULLIF(LineType,'') AS LineType,
                                    NULLIF(LineStyles,'') AS LineStyles,
                                    NULLIF(SheetSize,'') AS SheetSize
                                 FROM ContentWiseKeylineCoordinates
                                 WHERE ContentType = @ContentType AND Grain = @Grain AND UpsType = @UpsType AND ShapeName = @ShapeName
                                 ORDER BY CoordinateID";
            return await _connection.QueryAsync<KeylineCoordinateDto>(sql, new { ContentType = contentType, Grain = grain, UpsType = upsType, ShapeName = shapeName });
        }

        public async Task<IEnumerable<KeylineFormulaDto>> GetFormulasAsync()
        {
            const string sql = "SELECT ID, Formula FROM ContentWiseKeylineCoordinatesFormula ORDER BY ID";
            return await _connection.QueryAsync<KeylineFormulaDto>(sql);
        }

        public async Task<IEnumerable<string>> GetFormulaValuesAsync(string axis, string contentType, string grain, string upsType)
        {
            var col = axis.ToUpper() switch
            {
                "X1" => "AddinX1",
                "Y1" => "AddinY1",
                "X2" => "AddinX2",
                "Y2" => "AddinY2",
                _ => throw new ArgumentException("Invalid axis")
            };
            var sql = $"SELECT DISTINCT [{col}] FROM ContentWiseKeylineCoordinates WHERE ContentType = @ContentType AND Grain = @Grain AND UpsType = @UpsType AND [{col}] IS NOT NULL AND [{col}] <> ''";
            return await _connection.QueryAsync<string>(sql, new { ContentType = contentType, Grain = grain, UpsType = upsType });
        }

        public async Task<KeylineMetaDto> GetMetaAsync(string contentType, string grain, string upsType)
        {
            const string sql = @"
                SELECT DISTINCT ShapeName
                FROM ContentWiseKeylineCoordinates
                WHERE ContentType = @ContentType AND Grain = @Grain AND UpsType = @UpsType
                  AND ShapeName IS NOT NULL AND ShapeName <> ''
                ORDER BY ShapeName;

                SELECT DISTINCT AddinX1
                FROM ContentWiseKeylineCoordinates
                WHERE ContentType = @ContentType AND Grain = @Grain AND UpsType = @UpsType
                  AND AddinX1 IS NOT NULL AND AddinX1 <> '';

                SELECT DISTINCT AddinY1
                FROM ContentWiseKeylineCoordinates
                WHERE ContentType = @ContentType AND Grain = @Grain AND UpsType = @UpsType
                  AND AddinY1 IS NOT NULL AND AddinY1 <> '';

                SELECT DISTINCT AddinX2
                FROM ContentWiseKeylineCoordinates
                WHERE ContentType = @ContentType AND Grain = @Grain AND UpsType = @UpsType
                  AND AddinX2 IS NOT NULL AND AddinX2 <> '';

                SELECT DISTINCT AddinY2
                FROM ContentWiseKeylineCoordinates
                WHERE ContentType = @ContentType AND Grain = @Grain AND UpsType = @UpsType
                  AND AddinY2 IS NOT NULL AND AddinY2 <> '';";

            var p = new { ContentType = contentType, Grain = grain, UpsType = upsType };
            using var multi = await _connection.QueryMultipleAsync(sql, p);
            return new KeylineMetaDto
            {
                ShapeNames = await multi.ReadAsync<string>(),
                FormulaX1  = await multi.ReadAsync<string>(),
                FormulaY1  = await multi.ReadAsync<string>(),
                FormulaX2  = await multi.ReadAsync<string>(),
                FormulaY2  = await multi.ReadAsync<string>()
            };
        }

        public async Task SaveCoordinatesAsync(SaveCoordinatesRequest request)
        {
            if (_connection.State != ConnectionState.Open)
                await _connection.OpenAsync();

            using var tx = _connection.BeginTransaction();
            try
            {
                await _connection.ExecuteAsync(
                    "DELETE FROM ContentWiseKeylineCoordinates WHERE ContentType = @ContentName AND Grain = @Grain AND UpsType = @UpsType",
                    new { request.ContentName, request.Grain, request.UpsType }, tx);

                const string insertSql = @"INSERT INTO ContentWiseKeylineCoordinates
                    (ContentType, Grain, UpsType, ShapeType, ShapeName, LineType, AddInX1, AddInY1, AddInX2, AddInY2, AddInXForUps, AddInYForUps, LineStyles, SheetSize)
                    VALUES
                    (@ContentType, @Grain, @UpsType, @ShapeType, @ShapeName, @LineType, @AddInX1, @AddInY1, @AddInX2, @AddInY2, @AddInXForUps, @AddInYForUps, @LineStyles, @SheetSize)";

                foreach (var coord in request.Coordinates)
                {
                    coord.ContentType = request.ContentName;
                    coord.Grain = request.Grain;
                    coord.UpsType = request.UpsType;
                    await _connection.ExecuteAsync(insertSql, coord, tx);
                }

                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        public async Task SaveFormulaAsync(SaveFormulaRequest request)
        {
            if (request.EditFlag && request.FormulaID.HasValue)
            {
                await _connection.ExecuteAsync(
                    "UPDATE ContentWiseKeylineCoordinatesFormula SET Formula = @Formula WHERE ID = @ID",
                    new { request.Formula, ID = request.FormulaID });
            }
            else
            {
                await _connection.ExecuteAsync(
                    "INSERT INTO ContentWiseKeylineCoordinatesFormula (Formula) VALUES (@Formula)",
                    new { request.Formula });
            }
        }

        public async Task DeleteFormulaAsync(int formulaId)
        {
            await _connection.ExecuteAsync(
                "DELETE FROM ContentWiseKeylineCoordinatesFormula WHERE ID = @ID",
                new { ID = formulaId });
        }

        public async Task DeleteCoordinatesAsync(string contentName, string grain, string upsType)
        {
            await _connection.ExecuteAsync(
                "DELETE FROM ContentWiseKeylineCoordinates WHERE ContentType = @ContentName AND Grain = @Grain AND UpsType = @UpsType",
                new { ContentName = contentName, Grain = grain, UpsType = upsType });
        }

        public async Task<IEnumerable<KeylinePlanningDto>> GetPlanningAsync(string contentType)
        {
            const string sql = @"SELECT FormulaID, ContentType, Grain, UpsType, SheetSize, Formula
                                 FROM ContentWiseKeylineSheetPlanning
                                 WHERE ContentType = @ContentType
                                 ORDER BY FormulaID";
            return await _connection.QueryAsync<KeylinePlanningDto>(sql, new { ContentType = contentType });
        }

        public async Task SavePlanningAsync(SavePlanningRequest request)
        {
            if (_connection.State != ConnectionState.Open)
                await _connection.OpenAsync();

            using var tx = _connection.BeginTransaction();
            try
            {
                await _connection.ExecuteAsync(
                    "DELETE FROM ContentWiseKeylineSheetPlanning WHERE ContentType = @ContentName",
                    new { request.ContentName }, tx);

                const string insertSql = @"INSERT INTO ContentWiseKeylineSheetPlanning (ContentType, Grain, UpsType, SheetSize, Formula)
                                           VALUES (@ContentType, @Grain, @UpsType, @SheetSize, @Formula)";
                foreach (var plan in request.Planning)
                    await _connection.ExecuteAsync(insertSql, plan, tx);

                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        public async Task DeletePlanningAsync(string contentName)
        {
            await _connection.ExecuteAsync(
                "DELETE FROM ContentWiseKeylineSheetPlanning WHERE ContentType = @ContentName",
                new { ContentName = contentName });
        }
    }
}
