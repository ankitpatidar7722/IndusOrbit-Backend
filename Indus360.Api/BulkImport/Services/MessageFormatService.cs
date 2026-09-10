using Microsoft.Data.SqlClient;
using Dapper;
using Backend.DTOs;

namespace Backend.Services;

public class MessageFormatService : IMessageFormatService
{
    private readonly IConfiguration _config;

    public MessageFormatService(IConfiguration config)
    {
        _config = config;
    }

    private SqlConnection GetIndusConnection()
    {
        var connString = _config.GetConnectionString("IndusConnection");
        return new SqlConnection(connString);
    }

    public async Task<MessageFormatListResponse> GetAllActiveAsync()
    {
        try
        {
            using var conn = GetIndusConnection();

            // Create table if it doesn't exist
            await conn.ExecuteAsync(@"
                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'MessageFormatMaster')
                BEGIN
                    CREATE TABLE MessageFormatMaster (
                        MessageID INT IDENTITY(1,1) PRIMARY KEY,
                        MessageTitle NVARCHAR(200) NOT NULL,
                        MessageContent NVARCHAR(MAX) NOT NULL,
                        IsActive BIT NOT NULL DEFAULT 1
                    )
                END");

            var query = @"SELECT MessageID, MessageTitle, MessageContent, IsActive
                          FROM MessageFormatMaster
                          WHERE IsActive = 1
                          ORDER BY MessageTitle";

            var data = (await conn.QueryAsync<MessageFormatDto>(query)).ToList();
            return new MessageFormatListResponse { Success = true, Data = data };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MessageFormat] GetAllActive Error: {ex.Message}");
            return new MessageFormatListResponse { Success = false, Message = ex.Message };
        }
    }

    public async Task<MessageFormatResponse> CreateAsync(MessageFormatSaveRequest request)
    {
        try
        {
            using var conn = GetIndusConnection();

            // Create table if it doesn't exist
            await conn.ExecuteAsync(@"
                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'MessageFormatMaster')
                BEGIN
                    CREATE TABLE MessageFormatMaster (
                        MessageID INT IDENTITY(1,1) PRIMARY KEY,
                        MessageTitle NVARCHAR(200) NOT NULL,
                        MessageContent NVARCHAR(MAX) NOT NULL,
                        IsActive BIT NOT NULL DEFAULT 1
                    )
                END");

            var query = @"INSERT INTO MessageFormatMaster (MessageTitle, MessageContent, IsActive)
                          VALUES (@MessageTitle, @MessageContent, @IsActive);
                          SELECT CAST(SCOPE_IDENTITY() AS INT)";

            var newId = await conn.QuerySingleAsync<int>(query, new
            {
                request.MessageTitle,
                request.MessageContent,
                request.IsActive
            });

            var created = new MessageFormatDto
            {
                MessageID = newId,
                MessageTitle = request.MessageTitle,
                MessageContent = request.MessageContent,
                IsActive = request.IsActive
            };

            return new MessageFormatResponse { Success = true, Data = created, Message = "Message template created." };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MessageFormat] Create Error: {ex.Message}");
            return new MessageFormatResponse { Success = false, Message = ex.Message };
        }
    }

    public async Task<MessageFormatResponse> UpdateAsync(MessageFormatSaveRequest request)
    {
        try
        {
            using var conn = GetIndusConnection();
            var query = @"UPDATE MessageFormatMaster
                          SET MessageTitle = @MessageTitle,
                              MessageContent = @MessageContent,
                              IsActive = @IsActive
                          WHERE MessageID = @MessageID";

            await conn.ExecuteAsync(query, new
            {
                request.MessageID,
                request.MessageTitle,
                request.MessageContent,
                request.IsActive
            });

            return new MessageFormatResponse
            {
                Success = true,
                Message = "Message template updated.",
                Data = new MessageFormatDto
                {
                    MessageID = request.MessageID ?? 0,
                    MessageTitle = request.MessageTitle,
                    MessageContent = request.MessageContent,
                    IsActive = request.IsActive
                }
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MessageFormat] Update Error: {ex.Message}");
            return new MessageFormatResponse { Success = false, Message = ex.Message };
        }
    }

    public async Task<MessageFormatResponse> DeleteAsync(int messageId)
    {
        try
        {
            using var conn = GetIndusConnection();
            var query = @"UPDATE MessageFormatMaster SET IsActive = 0 WHERE MessageID = @MessageID";
            await conn.ExecuteAsync(query, new { MessageID = messageId });

            return new MessageFormatResponse { Success = true, Message = "Message template deleted." };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MessageFormat] Delete Error: {ex.Message}");
            return new MessageFormatResponse { Success = false, Message = ex.Message };
        }
    }
}
