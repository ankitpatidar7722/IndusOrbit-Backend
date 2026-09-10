using Backend.DTOs;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Backend.Services;

public class CompanyService : ICompanyService
{
    private readonly SqlConnection _connection;

    public CompanyService(SqlConnection connection)
    {
        _connection = connection;
    }

    public async Task<CompanyDto> GetCompanyAsync()
    {
        var query = @"
            SELECT TOP 1 
                CompanyID, CompanyName, Address1, Address2, Address3, City, State, Country, 
                Pincode, ContactNO, ConcerningPerson, MobileNO, Email,
                CompanyStartDate, IsActive, StateTinNo, IsSalesTax, LastInvoiceDate, 
                CINNo, TallyCompanyName, ProductionUnitAddress, CashAgainstDocumentsBankDetails, 
                Address, GSTIN, ProductionUnitName, PAN, IsDeletedTransaction, FAX, 
                ImportExportCode, BACKUPPATH, APIBaseURL, APIAuthenticationURL, 
                IsGstApplicable, IsVatApplicable, DefaultTaxLedgerTypeName, IsEinvoiceApplicable, 
                CurrencyHeadName, CurrencyChildName, CurrencyCode, CurrencySymboliconRef, 
                TaxApplicableBranchWise, EstimationRoundOffDecimalPlace, PurchaseRoundOffDecimalPlace, 
                InvoiceRoundOffDecimalPlace, CostEstimationMethodType, FlexoDomainEnable, 
                OffsetDomainEnable, CorrugationDomainEnable, RotoDomainEnable, 
                BookPlanningFeatureEnable, RigidBoxPlanningFeatureEnable, ShipperPlanningFeatureEnable, 
                IsInternalApprovalRequired, IECNo, BankDetails, IsRequisitionApproval, 
                IsPOApprovalRequired, IsInvoiceApprovalRequired, ApiClientID, ApiClientSecretID, 
                APIIntegrationRequired, IndusAPIAuthToken, ClientAPIAuthToken, IndusAPIBaseUrl, 
                ClientAPIBaseUrl, IndusTokenAuthAPI, ClientTokenAuthAPI, LogoutPage, 
                ByPassInventoryForProduction, ManualProductionEntryTime, InvoicePrintRDLC, 
                IsGRNApprovalRequired, PackingSlipPrintRDLC, ApplicationConfiguration, 
                JobScheduleReleaseRequired, ChallanPrintRDLC, PWOPrintRDLC, ProductionEntryBackDay, 
                ProductionUnitID, SalesReturnPrintRDLC, IndusMailAPIBaseUrl, ApiBasicAuthUserName, 
                ApiBasicAuthPassword, IsWastageAddInPrintingRate, MessageShow, Description, 
                Is_Book_Half_Form_Wastage, end_time, LastShownTime, Duration, 
                JobReleasedChecklistFeature, IsInvoicePrintProductWise, COAPrintRDLC, 
                IsInvoiceBlockFeatureRequired, PurchaseTolerance, IsSalesOrderApprovalRequired, 
                IsJobReleaseFeatureRequired, DesktopConnString, MSMENo, OTPVerificationExcludedDevices, 
                OTPVerificationFeatureEnabled, FastInvoicePrint, FastEInvoicePrint, 
                MultipleFYearNotRequired, WtCalculateOnEstimation, IsProductCatalogCreated, 
                EstimationPerUnitCostDecimalPlace, MaterialConsumptionDetailsFlage, TimeZone, 
                OutSourceChallanRDLC, 
                RoundOffImpressionValue, AutoRoundOffNotApplicable, ShowPlanUptoWastagePerc, 
                PWOFlexoPrintRDLC, IsSupplierItemAllocationRequired, ByPassCostApproval, 
                PWOGangPrintRDLC, QCAndPackingSlip, ItemSalesOrderBookingPrint, 
                RefCompanyCode, RefSalesOfficeCode, ISOTPREQUIRED, CompanyStaticIP
            FROM CompanyMaster
            WHERE IsDeletedTransaction = 0
            ORDER BY CompanyId ASC";
            
        if (_connection.State != System.Data.ConnectionState.Open)
            await _connection.OpenAsync();
            
        var company = await _connection.QueryFirstOrDefaultAsync<CompanyDto>(query);
        
        return company ?? new CompanyDto { CompanyName = "New Company" };
    }

    public async Task<bool> UpdateCompanyAsync(CompanyDto company)
    {
        var query = @"
            UPDATE CompanyMaster 
            SET CompanyName = @CompanyName,
                TallyCompanyName = @TallyCompanyName,
                ProductionUnitName = @ProductionUnitName,
                ProductionUnitAddress = @ProductionUnitAddress,
                Address = @Address,
                Address1 = @Address1,
                Address2 = @Address2,
                Address3 = @Address3,
                City = @City,
                State = @State,
                Country = @Country,
                Pincode = @Pincode,
                ContactNO = @ContactNO,
                MobileNO = @MobileNO,
                Email = @Email,
                ConcerningPerson = @ConcerningPerson,
                CompanyStartDate = @CompanyStartDate,
                IsActive = @IsActive,
                LastInvoiceDate = @LastInvoiceDate,
                GSTIN = @GSTIN,
                PAN = @PAN,
                CINNo = @CINNo,
                IECNo = @IECNo,
                ImportExportCode = @ImportExportCode,
                StateTinNo = @StateTinNo,
                MSMENo = @MSMENo,
                IsSalesTax = @IsSalesTax,
                IsGstApplicable = @IsGstApplicable,
                IsVatApplicable = @IsVatApplicable,
                IsEinvoiceApplicable = @IsEinvoiceApplicable,
                DefaultTaxLedgerTypeName = @DefaultTaxLedgerTypeName,
                TaxApplicableBranchWise = @TaxApplicableBranchWise,
                BankDetails = @BankDetails,
                CashAgainstDocumentsBankDetails = @CashAgainstDocumentsBankDetails,
                IsRequisitionApproval = @IsRequisitionApproval,
                IsPOApprovalRequired = @IsPOApprovalRequired,
                IsInvoiceApprovalRequired = @IsInvoiceApprovalRequired,
                IsGRNApprovalRequired = @IsGRNApprovalRequired,
                IsSalesOrderApprovalRequired = @IsSalesOrderApprovalRequired,
                IsJobReleaseFeatureRequired = @IsJobReleaseFeatureRequired,
                IsInternalApprovalRequired = @IsInternalApprovalRequired,
                ByPassCostApproval = @ByPassCostApproval,
                ByPassInventoryForProduction = @ByPassInventoryForProduction,
                JobReleasedChecklistFeature = @JobReleasedChecklistFeature,
                JobScheduleReleaseRequired = @JobScheduleReleaseRequired,
                FlexoDomainEnable = @FlexoDomainEnable,
                OffsetDomainEnable = @OffsetDomainEnable,
                CorrugationDomainEnable = @CorrugationDomainEnable,
                RotoDomainEnable = @RotoDomainEnable,
                BookPlanningFeatureEnable = @BookPlanningFeatureEnable,
                RigidBoxPlanningFeatureEnable = @RigidBoxPlanningFeatureEnable,
                ShipperPlanningFeatureEnable = @ShipperPlanningFeatureEnable,
                IsProductCatalogCreated = @IsProductCatalogCreated,
                IsSupplierItemAllocationRequired = @IsSupplierItemAllocationRequired,
                CostEstimationMethodType = @CostEstimationMethodType,
                EstimationRoundOffDecimalPlace = @EstimationRoundOffDecimalPlace,
                PurchaseRoundOffDecimalPlace = @PurchaseRoundOffDecimalPlace,
                InvoiceRoundOffDecimalPlace = @InvoiceRoundOffDecimalPlace,
                EstimationPerUnitCostDecimalPlace = @EstimationPerUnitCostDecimalPlace,
                RoundOffImpressionValue = @RoundOffImpressionValue,
                AutoRoundOffNotApplicable = @AutoRoundOffNotApplicable,
                WtCalculateOnEstimation = @WtCalculateOnEstimation,
                ShowPlanUptoWastagePerc = @ShowPlanUptoWastagePerc,
                IsWastageAddInPrintingRate = @IsWastageAddInPrintingRate,
                Is_Book_Half_Form_Wastage = @Is_Book_Half_Form_Wastage,
                InvoicePrintRDLC = @InvoicePrintRDLC,
                PackingSlipPrintRDLC = @PackingSlipPrintRDLC,
                ChallanPrintRDLC = @ChallanPrintRDLC,
                PWOPrintRDLC = @PWOPrintRDLC,
                SalesReturnPrintRDLC = @SalesReturnPrintRDLC,
                COAPrintRDLC = @COAPrintRDLC,
                OutSourceChallanRDLC = @OutSourceChallanRDLC,
                PWOFlexoPrintRDLC = @PWOFlexoPrintRDLC,
                PWOGangPrintRDLC = @PWOGangPrintRDLC,
                QCAndPackingSlip = @QCAndPackingSlip,
                ItemSalesOrderBookingPrint = @ItemSalesOrderBookingPrint,
                FastInvoicePrint = @FastInvoicePrint,
                FastEInvoicePrint = @FastEInvoicePrint,
                ManualProductionEntryTime = @ManualProductionEntryTime,
                ProductionEntryBackDay = @ProductionEntryBackDay,
                ProductionUnitID = @ProductionUnitID,
                MaterialConsumptionDetailsFlage = @MaterialConsumptionDetailsFlage,
                APIBaseURL = @APIBaseURL,
                APIAuthenticationURL = @APIAuthenticationURL,
                ApiClientID = @ApiClientID,
                ApiClientSecretID = @ApiClientSecretID,
                APIIntegrationRequired = @APIIntegrationRequired,
                IndusAPIAuthToken = @IndusAPIAuthToken,
                ClientAPIAuthToken = @ClientAPIAuthToken,
                IndusAPIBaseUrl = @IndusAPIBaseUrl,
                ClientAPIBaseUrl = @ClientAPIBaseUrl,
                IndusTokenAuthAPI = @IndusTokenAuthAPI,
                ClientTokenAuthAPI = @ClientTokenAuthAPI,
                IndusMailAPIBaseUrl = @IndusMailAPIBaseUrl,
                ApiBasicAuthUserName = @ApiBasicAuthUserName,
                ApiBasicAuthPassword = @ApiBasicAuthPassword,
                LogoutPage = @LogoutPage,
                DesktopConnString = @DesktopConnString,
                ApplicationConfiguration = @ApplicationConfiguration,
                CompanyStaticIP = @CompanyStaticIP,
                TimeZone = @TimeZone,
                Duration = @Duration,
                End_Time = @End_Time,
                LastShownTime = @LastShownTime,
                MessageShow = @MessageShow,
                OTPVerificationFeatureEnabled = @OTPVerificationFeatureEnabled,
                OTPVerificationExcludedDevices = @OTPVerificationExcludedDevices,
                MultipleFYearNotRequired = @MultipleFYearNotRequired,
                RefCompanyCode = @RefCompanyCode,
                RefSalesOfficeCode = @RefSalesOfficeCode,
                ISOTPREQUIRED = @ISOTPREQUIRED,
                CurrencyHeadName = @CurrencyHeadName,
                CurrencyChildName = @CurrencyChildName,
                CurrencyCode = @CurrencyCode,
                CurrencySymboliconRef = @CurrencySymboliconRef,
                FAX = @FAX,
                BACKUPPATH = @BACKUPPATH,
                Description = @Description,
                IsInvoicePrintProductWise = @IsInvoicePrintProductWise,
                IsInvoiceBlockFeatureRequired = @IsInvoiceBlockFeatureRequired,
                PurchaseTolerance = @PurchaseTolerance
            WHERE CompanyId = @CompanyId";

        if (_connection.State != System.Data.ConnectionState.Open)
            await _connection.OpenAsync();
        var rows = await _connection.ExecuteAsync(query, company);
        return rows > 0;
    }
}
