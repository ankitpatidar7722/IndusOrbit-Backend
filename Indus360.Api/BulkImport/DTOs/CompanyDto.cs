using System.Text.Json.Serialization;

namespace Backend.DTOs;

public class CompanyDto
{
    // Primary Key
    [JsonPropertyName("companyId")]
    public int CompanyId { get; set; }
    
    // 🏢 1. Company Basic Information
    [JsonPropertyName("companyName")]
    public string CompanyName { get; set; } = string.Empty;
    
    [JsonPropertyName("tallyCompanyName")]
    public string? TallyCompanyName { get; set; }
    
    [JsonPropertyName("productionUnitName")]
    public string? ProductionUnitName { get; set; }
    
    [JsonPropertyName("productionUnitAddress")]
    public string? ProductionUnitAddress { get; set; }
    
    [JsonPropertyName("address")]
    public string Address { get; set; } = string.Empty;
    
    [JsonPropertyName("address1")]
    public string? Address1 { get; set; }
    
    [JsonPropertyName("address2")]
    public string? Address2 { get; set; }
    
    [JsonPropertyName("address3")]
    public string? Address3 { get; set; }
    
    [JsonPropertyName("city")]
    public string? City { get; set; }
    
    [JsonPropertyName("state")]
    public string? State { get; set; }
    
    [JsonPropertyName("country")]
    public string? Country { get; set; }
    
    [JsonPropertyName("pincode")]
    public string? Pincode { get; set; }
    
    [JsonPropertyName("contactNO")]
    public string? ContactNO { get; set; }
    
    [JsonPropertyName("mobileNO")]
    public string? MobileNO { get; set; }
    
    [JsonPropertyName("phone")]
    public string Phone { get; set; } = string.Empty;
    
    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;
    
    [JsonPropertyName("website")]
    public string Website { get; set; } = string.Empty;
    
    [JsonPropertyName("concerningPerson")]
    public string? ConcerningPerson { get; set; }
    
    [JsonPropertyName("companyStartDate")]
    public DateTime? CompanyStartDate { get; set; }
    
    [JsonPropertyName("isActive")]
    public bool IsActive { get; set; }
    
    [JsonPropertyName("lastInvoiceDate")]
    public DateTime? LastInvoiceDate { get; set; }
    
    // 🧾 2. Statutory & Tax Information
    [JsonPropertyName("gstin")]
    public string GSTIN { get; set; } = string.Empty;
    
    [JsonPropertyName("pan")]
    public string? PAN { get; set; }
    
    [JsonPropertyName("cinNo")]
    public string? CINNo { get; set; }
    
    [JsonPropertyName("iecNo")]
    public string? IECNo { get; set; }
    
    [JsonPropertyName("importExportCode")]
    public string? ImportExportCode { get; set; }
    
    [JsonPropertyName("stateTinNo")]
    public string? StateTinNo { get; set; }
    
    [JsonPropertyName("msmeno")]
    public string? MSMENo { get; set; }
    
    [JsonPropertyName("isSalesTax")]
    public bool IsSalesTax { get; set; }
    
    [JsonPropertyName("isGstApplicable")]
    public bool IsGstApplicable { get; set; }
    
    [JsonPropertyName("isVatApplicable")]
    public bool IsVatApplicable { get; set; }
    
    [JsonPropertyName("isEinvoiceApplicable")]
    public bool IsEinvoiceApplicable { get; set; }
    
    [JsonPropertyName("defaultTaxLedgerTypeName")]
    public string? DefaultTaxLedgerTypeName { get; set; }
    
    [JsonPropertyName("taxApplicableBranchWise")]
    public bool TaxApplicableBranchWise { get; set; }
    
    // 🏦 3. Bank & Payment Information
    [JsonPropertyName("bankDetails")]
    public string? BankDetails { get; set; }
    
    [JsonPropertyName("cashAgainstDocumentsBankDetails")]
    public string? CashAgainstDocumentsBankDetails { get; set; }
    
    // 🔐 4. Approval & Workflow Settings
    [JsonPropertyName("isRequisitionApproval")]
    public bool IsRequisitionApproval { get; set; }
    
    [JsonPropertyName("isPOApprovalRequired")]
    public bool IsPOApprovalRequired { get; set; }
    
    [JsonPropertyName("isInvoiceApprovalRequired")]
    public bool IsInvoiceApprovalRequired { get; set; }
    
    [JsonPropertyName("isGRNApprovalRequired")]
    public bool IsGRNApprovalRequired { get; set; }
    
    [JsonPropertyName("isSalesOrderApprovalRequired")]
    public bool IsSalesOrderApprovalRequired { get; set; }
    
    [JsonPropertyName("isJobReleaseFeatureRequired")]
    public bool IsJobReleaseFeatureRequired { get; set; }
    
    [JsonPropertyName("isInternalApprovalRequired")]
    public bool IsInternalApprovalRequired { get; set; }
    
    [JsonPropertyName("byPassCostApproval")]
    public bool ByPassCostApproval { get; set; }
    
    [JsonPropertyName("byPassInventoryForProduction")]
    public bool ByPassInventoryForProduction { get; set; }
    
    [JsonPropertyName("jobReleasedChecklistFeature")]
    public bool JobReleasedChecklistFeature { get; set; }
    
    [JsonPropertyName("jobScheduleReleaseRequired")]
    public bool JobScheduleReleaseRequired { get; set; }
    
    // ⚙️ 5. Domain / Module Enable Settings
    [JsonPropertyName("flexoDomainEnable")]
    public bool? FlexoDomainEnable { get; set; }
    
    [JsonPropertyName("offsetDomainEnable")]
    public bool? OffsetDomainEnable { get; set; }
    
    [JsonPropertyName("corrugationDomainEnable")]
    public bool? CorrugationDomainEnable { get; set; }
    
    [JsonPropertyName("rotoDomainEnable")]
    public bool? RotoDomainEnable { get; set; }
    
    [JsonPropertyName("bookPlanningFeatureEnable")]
    public bool? BookPlanningFeatureEnable { get; set; }
    
    [JsonPropertyName("rigidBoxPlanningFeatureEnable")]
    public bool? RigidBoxPlanningFeatureEnable { get; set; }
    
    [JsonPropertyName("shipperPlanningFeatureEnable")]
    public bool? ShipperPlanningFeatureEnable { get; set; }
    
    [JsonPropertyName("isProductCatalogCreated")]
    public bool? IsProductCatalogCreated { get; set; }
    
    [JsonPropertyName("isSupplierItemAllocationRequired")]
    public bool? IsSupplierItemAllocationRequired { get; set; }
    
    // 🧮 6. Estimation & Calculation Settings
    [JsonPropertyName("costEstimationMethodType")]
    public string? CostEstimationMethodType { get; set; }
    
    [JsonPropertyName("estimationRoundOffDecimalPlace")]
    public int? EstimationRoundOffDecimalPlace { get; set; }
    
    [JsonPropertyName("purchaseRoundOffDecimalPlace")]
    public int? PurchaseRoundOffDecimalPlace { get; set; }
    
    [JsonPropertyName("invoiceRoundOffDecimalPlace")]
    public int? InvoiceRoundOffDecimalPlace { get; set; }
    
    [JsonPropertyName("estimationPerUnitCostDecimalPlace")]
    public int? EstimationPerUnitCostDecimalPlace { get; set; }
    
    [JsonPropertyName("roundOffImpressionValue")]
    public decimal? RoundOffImpressionValue { get; set; }
    
    [JsonPropertyName("autoRoundOffNotApplicable")]
    public bool? AutoRoundOffNotApplicable { get; set; }
    
    [JsonPropertyName("wtCalculateOnEstimation")]
    public string? WtCalculateOnEstimation { get; set; }
    
    [JsonPropertyName("showPlanUptoWastagePerc")]
    public decimal? ShowPlanUptoWastagePerc { get; set; }
    
    [JsonPropertyName("isWastageAddInPrintingRate")]
    public bool? IsWastageAddInPrintingRate { get; set; }
    
    [JsonPropertyName("is_Book_Half_Form_Wastage")]
    public bool? Is_Book_Half_Form_Wastage { get; set; }
    
    // 🖨️ 7. Printing / RDLC Settings
    [JsonPropertyName("invoicePrintRDLC")]
    public string? InvoicePrintRDLC { get; set; }
    
    [JsonPropertyName("packingSlipPrintRDLC")]
    public string? PackingSlipPrintRDLC { get; set; }
    
    [JsonPropertyName("challanPrintRDLC")]
    public string? ChallanPrintRDLC { get; set; }
    
    [JsonPropertyName("pwoPrintRDLC")]
    public string? PWOPrintRDLC { get; set; }
    
    [JsonPropertyName("salesReturnPrintRDLC")]
    public string? SalesReturnPrintRDLC { get; set; }
    
    [JsonPropertyName("coaPrintRDLC")]
    public string? COAPrintRDLC { get; set; }
    
    [JsonPropertyName("outSourceChallanRDLC")]
    public string? OutSourceChallanRDLC { get; set; }
    
    [JsonPropertyName("pwoFlexoPrintRDLC")]
    public string? PWOFlexoPrintRDLC { get; set; }
    
    [JsonPropertyName("pwoGangPrintRDLC")]
    public string? PWOGangPrintRDLC { get; set; }
    
    [JsonPropertyName("qcAndPackingSlip")]
    public string? QCAndPackingSlip { get; set; }
    
    [JsonPropertyName("itemSalesOrderBookingPrint")]
    public string? ItemSalesOrderBookingPrint { get; set; }
    
    [JsonPropertyName("unitwisePrintoutSetting")]
    public bool? UnitwisePrintoutSetting { get; set; }
    
    [JsonPropertyName("fastInvoicePrint")]
    public string? FastInvoicePrint { get; set; }

    [JsonPropertyName("fastEInvoicePrint")]
    public string? FastEInvoicePrint { get; set; }
    
    // 🏭 8. Production Configuration
    [JsonPropertyName("manualProductionEntryTime")]
    public bool? ManualProductionEntryTime { get; set; }
    
    [JsonPropertyName("productionEntryBackDay")]
    public string? ProductionEntryBackDay { get; set; }
    
    [JsonPropertyName("productionUnitID")]
    public int? ProductionUnitID { get; set; }
    
    [JsonPropertyName("generateVoucherNoByProductionUnit")]
    public bool? GenerateVoucherNoByProductionUnit { get; set; }
    
    [JsonPropertyName("materialConsumptionDetailsFlage")]
    public bool? MaterialConsumptionDetailsFlage { get; set; }
    
    [JsonPropertyName("bufferGSMMinus")]
    public decimal? BufferGSMMinus { get; set; }
    
    [JsonPropertyName("bufferGSMPlus")]
    public decimal? BufferGSMPlus { get; set; }
    
    [JsonPropertyName("bufferSizeMinus")]
    public decimal? BufferSizeMinus { get; set; }
    
    [JsonPropertyName("bufferSizePlus")]
    public decimal? BufferSizePlus { get; set; }
    
    // 🌐 9. API & Integration Settings
    [JsonPropertyName("apiBaseURL")]
    public string? APIBaseURL { get; set; }
    
    [JsonPropertyName("apiAuthenticationURL")]
    public string? APIAuthenticationURL { get; set; }
    
    [JsonPropertyName("apiClientID")]
    public string? ApiClientID { get; set; }
    
    [JsonPropertyName("apiClientSecretID")]
    public string? ApiClientSecretID { get; set; }
    
    [JsonPropertyName("apiIntegrationRequired")]
    public bool? APIIntegrationRequired { get; set; }
    
    [JsonPropertyName("indusAPIAuthToken")]
    public string? IndusAPIAuthToken { get; set; }
    
    [JsonPropertyName("clientAPIAuthToken")]
    public string? ClientAPIAuthToken { get; set; }
    
    [JsonPropertyName("indusAPIBaseUrl")]
    public string? IndusAPIBaseUrl { get; set; }
    
    [JsonPropertyName("clientAPIBaseUrl")]
    public string? ClientAPIBaseUrl { get; set; }
    
    [JsonPropertyName("indusTokenAuthAPI")]
    public string? IndusTokenAuthAPI { get; set; }
    
    [JsonPropertyName("clientTokenAuthAPI")]
    public string? ClientTokenAuthAPI { get; set; }
    
    [JsonPropertyName("indusMailAPIBaseUrl")]
    public string? IndusMailAPIBaseUrl { get; set; }
    
    [JsonPropertyName("apiBasicAuthUserName")]
    public string? ApiBasicAuthUserName { get; set; }
    
    [JsonPropertyName("apiBasicAuthPassword")]
    public string? ApiBasicAuthPassword { get; set; }
    
    [JsonPropertyName("integrationType")]
    public string? IntegrationType { get; set; }
    
    [JsonPropertyName("logoutPage")]
    public string? LogoutPage { get; set; }
    
    [JsonPropertyName("desktopConnString")]
    public string? DesktopConnString { get; set; }
    
    [JsonPropertyName("applicationConfiguration")]
    public string? ApplicationConfiguration { get; set; }
    
    [JsonPropertyName("companyStaticIP")]
    public string? CompanyStaticIP { get; set; }
    
    // 🕒 11. Time & System Settings
    [JsonPropertyName("timeZone")]
    public string? TimeZone { get; set; }
    
    [JsonPropertyName("duration")]
    public string? Duration { get; set; }
    
    [JsonPropertyName("end_time")]
    public string? End_Time { get; set; }
    
    [JsonPropertyName("lastShownTime")]
    public string? LastShownTime { get; set; }
    
    [JsonPropertyName("messageShow")]
    public string? MessageShow { get; set; }
    
    [JsonPropertyName("time")]
    public string? Time { get; set; }
    
    // 🔐 12. Security & OTP Settings
    [JsonPropertyName("otpVerificationFeatureEnabled")]
    public bool? OTPVerificationFeatureEnabled { get; set; }
    
    [JsonPropertyName("otpVerificationExcludedDevices")]
    public string? OTPVerificationExcludedDevices { get; set; }
    
    [JsonPropertyName("multipleFYearNotRequired")]
    public bool? MultipleFYearNotRequired { get; set; }
    
    // 🏷️ 13. Prefix & Reference Settings
    [JsonPropertyName("refCompanyCode")]
    public string? RefCompanyCode { get; set; }
    
    [JsonPropertyName("refSalesOfficeCode")]
    public string? RefSalesOfficeCode { get; set; }
    
    [JsonPropertyName("isotpRequired")]
    public bool? ISOTPREQUIRED { get; set; }
    
    // 💱 14. Currency Settings
    [JsonPropertyName("currencyHeadName")]
    public string? CurrencyHeadName { get; set; }
    
    [JsonPropertyName("currencyChildName")]
    public string? CurrencyChildName { get; set; }
    
    [JsonPropertyName("currencyCode")]
    public string? CurrencyCode { get; set; }
    
    [JsonPropertyName("currencySymboliconRef")]
    public string? CurrencySymboliconRef { get; set; }
    
    // 📝 15. Miscellaneous / Other Settings
    [JsonPropertyName("fax")]
    public string? FAX { get; set; }
    
    [JsonPropertyName("backupPath")]
    public string? BACKUPPATH { get; set; }
    
    [JsonPropertyName("description")]
    public string? Description { get; set; }
    
    [JsonPropertyName("isInvoicePrintProductWise")]
    public bool? IsInvoicePrintProductWise { get; set; }
    
    [JsonPropertyName("isInvoiceBlockFeatureRequired")]
    public bool? IsInvoiceBlockFeatureRequired { get; set; }
    
    [JsonPropertyName("purchaseTolerance")]
    public decimal? PurchaseTolerance { get; set; }
    
    [JsonPropertyName("isDeletedTransaction")]
    public bool IsDeletedTransaction { get; set; }

    // 🏭 Production Extras
    [JsonPropertyName("isProductionSlipGenerated")]
    public bool? IsProductionSlipGenerated { get; set; }

    [JsonPropertyName("productionProcessWiseToleranceRequired")]
    public bool? ProductionProcessWiseToleranceRequired { get; set; }

    // 💬 Communication & CRM
    [JsonPropertyName("isCrmActivated")]
    public bool? IsCrmActivated { get; set; }

    [JsonPropertyName("isWhatsAppActivated")]
    public bool? IsWhatsAppActivated { get; set; }

    [JsonPropertyName("isEmailActivated")]
    public bool? IsEmailActivated { get; set; }

    [JsonPropertyName("isNotificationEnabled")]
    public bool? IsNotificationEnabled { get; set; }

    // 📢 Client Communication
    [JsonPropertyName("isJobScheduled_SendToClient")]
    public bool? IsJobScheduled_SendToClient { get; set; }

    [JsonPropertyName("isOrderReady_QcAndPacking_SendToClient")]
    public bool? IsOrderReady_QcAndPacking_SendToClient { get; set; }

    [JsonPropertyName("isInvoice_Ready_SendToClient")]
    public bool? IsInvoice_Ready_SendToClient { get; set; }

    [JsonPropertyName("isSales_Order_Approve_ByClient")]
    public bool? IsSales_Order_Approve_ByClient { get; set; }

    // 🔄 Workflow Automation
    [JsonPropertyName("isAutoRequisitionCreation")]
    public long? IsAutoRequisitionCreation { get; set; }

    [JsonPropertyName("autoIndentFeatureRequired")]
    public bool? AutoIndentFeatureRequired { get; set; }

    [JsonPropertyName("isPicklistFeatureRequired")]
    public bool? IsPicklistFeatureRequired { get; set; }

    // 📄 Document Extras
    [JsonPropertyName("isQuotationVisibleAfterSO")]
    public bool? IsQuotationVisibleAfterSO { get; set; }

    // 🏷️ Prefix Settings
    [JsonPropertyName("productCatlogPrefix")]
    public string? ProductCatlogPrefix { get; set; }

    [JsonPropertyName("jobCardPrefix")]
    public string? JobCardPrefix { get; set; }
}
