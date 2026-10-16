using LMS.Identity.BusinessSerive.Interfaces;
using LMS.Identity.BusinessSerive.Data;
using LMS.Identity.DTO.Entities;
using Microsoft.EntityFrameworkCore;
using LMS.Core.Repository.UnitOfWork;
using AutoMapper;
using LMS.Identity.DTO;
using System;
using System.Threading.Tasks;
using System.Threading;
using LMS.Identity.Utilities;
using System.Net;
using LMS.Identity.BusinessSerive.Common;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using MailKit.Security;
using MimeKit;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.Data.SqlClient;

namespace LMS.Identity.BusinessSerive.Services
{
    public class LoginService : ILoginService
    {
        // Prevent concurrent registration requests for the same email from creating
        // duplicate tenant records. SemaphoreSlim per-email serializes Register calls
        // for a given email address in-process.
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _registrationLocks = new();

        private readonly IMapper _mapper;
        private readonly IUnitOfWork<IdentityDbContext> _unitOfWork;
        private readonly IMailConfiguration _mailConfiguration;
        private readonly string _mailSubject;
        private readonly string _mailFrom;
        private readonly ILogger<LoginService> _logger;

        public LoginService(IMailConfiguration mailConfiguration, IMapper mapper, IUnitOfWork<IdentityDbContext> unitOfWork, ILogger<LoginService> logger)
        {
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _mailConfiguration = mailConfiguration;
            _mailFrom = _mail_configuration_safe(mailConfiguration);
            _mailSubject = _mail_configuration_subject(mailConfiguration);
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // Small helpers to avoid potential null reference when reading mail config in ctor
        private static string _mail_configuration_safe(IMailConfiguration cfg) => cfg?.MailFrom ?? string.Empty;
        private static string _mail_configuration_subject(IMailConfiguration cfg) => cfg?.MailSubject ?? string.Empty;

        private static bool IsLegacySubject(string subject)
            => string.Equals((subject ?? string.Empty).Trim(), "Registration Status Report", StringComparison.OrdinalIgnoreCase)
               || string.Equals((subject ?? string.Empty).Trim(), "Registration Status Alert", StringComparison.OrdinalIgnoreCase);

        public async Task<ActionReturnType> Register(TenantDto tenantDto)
        {
            try
            {
                _logger?.LogInformation("Register called for Email={Email}, TenantName={TenantName}, NoOfStores={NoOfStores}", tenantDto?.Email, tenantDto?.TenantName, tenantDto?.NoOfStores);

                if (tenantDto == null)
                {
                    return ActionSet.ActionReturnType(HttpStatusCode.NoContent, new IOResponse { StatusCode = "204", TenantId = "", Message = IdentityValidationMessage.IDENTITY_DATANOTFOUND_ERROR_MESSAGE });
                }
                else if (tenantDto != null)
                {
                if (string.IsNullOrWhiteSpace(tenantDto.Email)
                    || string.IsNullOrWhiteSpace(tenantDto.TenantName)
                    || tenantDto.NoOfStores <= 0)
                {
                    return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new IOResponse
                    {
                        StatusCode = "400",
                        TenantId = string.Empty,
                        Message = "Tenant name, email, and number of stores are required."
                    });
                }

                tenantDto.Email = tenantDto.Email.Trim();
                tenantDto.TenantName = tenantDto.TenantName.Trim();

                // Serialize concurrent registrations for the same email so two
                // simultaneous requests cannot both create a tenant row. Hold the
                // lock through persistence and tenant-store creation.
                var normalizedEmailKey = tenantDto.Email.ToLowerInvariant();
                var sem = _registrationLocks.GetOrAdd(normalizedEmailKey, _ => new SemaphoreSlim(1, 1));
                await sem.WaitAsync();
                try
                {
                    var result = await _unitOfWork.DbContext.Tenants.FirstOrDefaultAsync(em => em.Email == tenantDto.Email);
                    if (result != null)
                    {
                        return ActionSet.ActionReturnType(HttpStatusCode.AlreadyReported, new IOResponse { StatusCode = "208", Message = IdentityValidationMessage.IDENTITY_DATAFOUND_ERROR_MESSAGE });
                    }

                var tenantData = _mapper.Map<TenantEntity>(tenantDto);
                // Assign TenantId early so tenant-level configuration can reference it atomically.
                var guid = Guid.NewGuid();
                var tenantid = Convert.ToString(guid);
                tenantData.TenantId = tenantid;
                tenantData.ModifiedDate = DateTime.UtcNow;
                tenantData.CreatedDate = DateTime.UtcNow;
                // New tenants stay pending until a super admin approves them.
                tenantData.ApprovalStatus = TenantApprovalStatus.Pending;

                // Build tenant-level configuration (StoreCode = empty) and persist it together with tenant to avoid partial state.
                // Build configuration JSON for tenant-level (StoreCode = empty). TenantName intentionally set to tenant email per API contract.
                // Normalize currency inputs from the incoming DTO so values provided by the UI are preserved.
                var providedCurrencyCode = tenantDto.CurrencyCode?.Trim();
                var providedCurrencySymbol = tenantDto.CurrencySymbol?.Trim();
                var providedCurrencyLocale = tenantDto.CurrencyLocale?.Trim();
                int? providedCurrencyDigits = tenantDto.CurrencyDecimalDigits;

                var defaultCfg = CountryCurrencyMapping.GetDefaultForCountry(tenantDto.Country);

                var configurationJson = Newtonsoft.Json.JsonConvert.SerializeObject(new
                {
                    TenantName = tenantDto.Email,
                    StoreCode = string.Empty,
                    CurrencyCode = string.IsNullOrWhiteSpace(providedCurrencyCode) ? defaultCfg?.CurrencyCode : providedCurrencyCode,
                    CurrencySymbol = string.IsNullOrWhiteSpace(providedCurrencySymbol) ? defaultCfg?.CurrencySymbol : providedCurrencySymbol,
                    CurrencyLocale = string.IsNullOrWhiteSpace(providedCurrencyLocale) ? defaultCfg?.CurrencyLocale : providedCurrencyLocale,
                    CurrencyDecimalDigits = providedCurrencyDigits ?? defaultCfg?.DecimalDigits
                });

                // Add both tenant and tenant-level configuration and save in one transaction so failure leaves no partial state.
                _unitOfWork.DbContext.Tenants.Add(tenantData);
                var cfgEntity = new LMS.Identity.DTO.Entities.StoreConfigurationEntity
                {
                    TenantId = tenantData.TenantId,
                    StoreCode = string.Empty,
                    ConfigurationJson = configurationJson,
                    CurrencyCode = string.IsNullOrWhiteSpace(providedCurrencyCode) ? defaultCfg?.CurrencyCode : providedCurrencyCode,
                    CurrencySymbol = string.IsNullOrWhiteSpace(providedCurrencySymbol) ? defaultCfg?.CurrencySymbol : providedCurrencySymbol,
                    CurrencyLocale = string.IsNullOrWhiteSpace(providedCurrencyLocale) ? defaultCfg?.CurrencyLocale : providedCurrencyLocale,
                    CurrencyDecimalDigits = providedCurrencyDigits ?? defaultCfg?.DecimalDigits,
                    CreatedDate = DateTime.UtcNow,
                    ModifiedDate = DateTime.UtcNow,
                    CreatedBy = tenantDto.Email,
                    ModifiedBy = tenantDto.Email
                };
                _unitOfWork.DbContext.StoreConfigurations.Add(cfgEntity);
                // Persist tenant and tenant-level configuration immediately so
                // database-level unique constraint violations are detected here.
                try
                {
                    await _unitOfWork.SaveChangesAsync();
                }
                catch (DbUpdateException dbEx)
                {
                    // If this is a SQL Server duplicate-key error for the Tenants.Email
                    // unique index, return the existing 208 duplicate response.
                    if (IsSqlServerDuplicateKeyException(dbEx))
                    {
                        _logger?.LogWarning(dbEx, "Duplicate tenant detected while saving Tenant.Email={Email}", tenantDto?.Email);
                        // Detach any newly-added entities so the DbContext isn't left in a bad state.
                        try
                        {
                            var added = _unitOfWork.DbContext.ChangeTracker.Entries()
                                .Where(e => e.State == EntityState.Added)
                                .ToList();
                            foreach (var ent in added)
                            {
                                ent.State = EntityState.Detached;
                            }
                        }
                        catch (Exception detEx)
                        {
                            _logger?.LogWarning(detEx, "Failed to detach entries after duplicate key error for Email={Email}", tenantDto?.Email);
                        }

                        return ActionSet.ActionReturnType(HttpStatusCode.AlreadyReported, new IOResponse { StatusCode = "208", Message = IdentityValidationMessage.IDENTITY_DATAFOUND_ERROR_MESSAGE });
                    }

                    throw;
                }

                // Helper: determine whether a DbUpdateException was caused by a SQL Server
                // unique constraint violation on Tenants.Email. Checks SqlException numbers
                // 2627 (unique constraint) and 2601 (unique index) and looks for the Email
                // or IX_Tenants_Email text in the message when available.
                static bool IsSqlServerDuplicateKeyException(DbUpdateException ex)
                {
                    if (ex == null) return false;
                    // Inspect inner exceptions for SqlException
                    Exception inner = ex;
                    while (inner != null)
                    {
                        if (inner is SqlException sqlEx)
                        {
                            // SQL Server error numbers for unique constraint/index violations
                            if (sqlEx.Number == 2627 || sqlEx.Number == 2601)
                            {
                                // Best-effort: check if the message references the Tenants table or Email column
                                var msg = sqlEx.Message ?? string.Empty;
                                if (msg.IndexOf("Tenants", StringComparison.OrdinalIgnoreCase) >= 0
                                    || msg.IndexOf("IX_Tenants_Email", StringComparison.OrdinalIgnoreCase) >= 0
                                    || msg.IndexOf("Email", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    return true;
                                }
                                return true;
                            }
                        }

                        if (inner is DbException dbEx2)
                        {
                            // If other providers wrap SQL Server error, inspect message
                            var msg2 = dbEx2.Message ?? string.Empty;
                            if ((msg2.IndexOf("unique", StringComparison.OrdinalIgnoreCase) >= 0
                                 || msg2.IndexOf("duplicate", StringComparison.OrdinalIgnoreCase) >= 0)
                                 && msg2.IndexOf("Email", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                return true;
                            }
                        }

                        inner = inner.InnerException;
                    }

                    return false;
                }
            // Keep TenantStoreInfos.Status in sync with tenant approval state so the
            // approval and the store info status are persisted together atomically.
            var tenantStoreInfo = await GetTenantStoreByTenantIdAsync(tenantid);
            if (tenantStoreInfo != null)
            {
                // New tenant starts as Pending; only Approved tenants are Active.
                tenantStoreInfo.Status = tenantData.ApprovalStatus == TenantApprovalStatus.Approved
                    ? "Active"
                    : tenantData.ApprovalStatus == TenantApprovalStatus.Rejected
                        ? "InActive"
                        : tenantStoreInfo.Status;
            }

                try
                {
                    // Create tenant user/store info and start email delivery in background.
                    // Execute within the semaphore to avoid races that could produce
                    // duplicate TenantStoreInfos rows. Start CreateTenantUserInfoAsync
                    // synchronously here to ensure TenantStoreInfos rows are created
                    // before releasing the semaphore; any long-running email send is
                    // performed internally by SendEmailToRegisterTenantAsync which
                    // already logs failures and does not throw to callers.
                    await CreateTenantUserInfoAsync(tenantDto, tenantid);
                }
                catch (Exception ex)
                {
                    // If tenant store creation or email sending fails, log error but
                    // do not fail the registration. Email sending may be slow; errors
                    // are logged for diagnostics.
                    _logger?.LogError(ex, "CreateTenantUserInfoAsync failed for TenantId={TenantId}", tenantid);
                }

                var registrationMessage = IdentityValidationMessage.IDENTITY_INSERT_SUCCESS_MESSAGE;

                // Ensure tenant-level configuration StoreCode reflects an existing assigned store code
                // Do not generate a new code here; use the first assigned store code if present.
                // Reload the tenant store info and tenant-level configuration from the database
                // and update the existing persisted StoreConfiguration row with the first assigned store code.
                try
                {
                    _logger?.LogInformation("Fetching TenantStoreInfo for TenantId={TenantId}", tenantid);
                    // Check DB connectivity first to fail fast and produce useful logs
                    try
                    {
                        var canConnect = await _unitOfWork.DbContext.Database.CanConnectAsync();
                        _logger?.LogInformation("Database connectivity: {CanConnect}", canConnect);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Database connectivity check failed before fetching TenantStoreInfo for TenantId={TenantId}", tenantid);
                    }

                    // Use a short cancellation timeout to avoid hanging indefinitely while debugging
                    TenantStoreInfoEntity tenantStoreInfoAfter = null;
                    try
                    {
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                        tenantStoreInfoAfter = await _unitOfWork.DbContext.TenantStoreInfos
                            .AsNoTracking()
                            .FirstOrDefaultAsync(tsi => tsi.TenantId == tenantid, cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        _logger?.LogWarning("Timeout while fetching TenantStoreInfo for TenantId={TenantId}", tenantid);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Error fetching TenantStoreInfo for TenantId={TenantId}", tenantid);
                    }
                    var firstStoreCode = (tenantStoreInfoAfter?.Storecodes ?? new List<string>()).FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(firstStoreCode))
                    {
                        var normalizedCode = firstStoreCode.Trim();

                        // Load the persisted tenant-level configuration (StoreCode was saved as empty string at registration)
                        var persistedCfg = await _unitOfWork.DbContext.StoreConfigurations
                            .FirstOrDefaultAsync(x => x.TenantId == tenantid && (x.StoreCode == null || x.StoreCode == string.Empty));

                        if (persistedCfg != null && string.IsNullOrWhiteSpace(persistedCfg.StoreCode))
                        {
                            persistedCfg.StoreCode = normalizedCode;

                            // Update ConfigurationJson StoreCode property if present
                            try
                            {
                                var j = JObject.Parse(persistedCfg.ConfigurationJson ?? string.Empty);
                                j["StoreCode"] = normalizedCode;
                                // If currency properties are present in the JSON, copy them to the dedicated columns
                                try
                                {
                                    var cc = j["CurrencyCode"]?.ToString();
                                    if (!string.IsNullOrWhiteSpace(cc)) persistedCfg.CurrencyCode = cc;
                                    var cs = j["CurrencySymbol"]?.ToString();
                                    if (!string.IsNullOrWhiteSpace(cs)) persistedCfg.CurrencySymbol = cs;
                                    var cl = j["CurrencyLocale"]?.ToString();
                                    if (!string.IsNullOrWhiteSpace(cl)) persistedCfg.CurrencyLocale = cl;
                                    if (j["CurrencyDecimalDigits"] != null && int.TryParse(j["CurrencyDecimalDigits"].ToString(), out var digs)) persistedCfg.CurrencyDecimalDigits = digs;
                                }
                                catch { /* swallow secondary parse errors to avoid failing the whole update */ }

                                persistedCfg.ConfigurationJson = j.ToString(Newtonsoft.Json.Formatting.None);
                            }
                            catch
                            {
                                // If parsing fails, regenerate minimal JSON with the new StoreCode while preserving TenantName when possible
                                string tenantName = tenantDto?.Email ?? string.Empty;
                                var cfgObj = new { TenantName = tenantName, StoreCode = normalizedCode };
                                persistedCfg.ConfigurationJson = Newtonsoft.Json.JsonConvert.SerializeObject(cfgObj);
                            }

                            await _unitOfWork.SaveChangesAsync();
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to update tenant-level StoreConfiguration StoreCode for TenantId {TenantId}", tenantid);
                }

                // Note: semaphore is released in the outer finally block to ensure a
                // single release happens exactly once for the local 'sem' instance.

                // Return 200 OK for success. The response body contains the TenantId
                // and a human-friendly message. The per-email semaphore was held for
                // the duration of persistence and tenant store creation to prevent
                // concurrent duplicates.
                return ActionSet.ActionReturnType(HttpStatusCode.OK, new IOResponse { StatusCode = "200", TenantId = tenantid, Message = registrationMessage, TenantName = tenantDto.TenantName });
                }
                finally
                {
                    // release the semaphore so other attempts for this email can proceed
                    try { sem.Release(); } catch { }
                }
            }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Unhandled exception in Register for Email={Email}", tenantDto?.Email);
                return ActionSet.ActionReturnType(HttpStatusCode.InternalServerError, new IOResponse { StatusCode = "500", TenantId = "", Message = IdentityValidationMessage.IDENTITY_INSERT_ERROR_MESSAGE });
            }
            // Fallback return to satisfy all control-flow paths. In practice the catch above returns for exceptions.
            return ActionSet.ActionReturnType(HttpStatusCode.InternalServerError, new IOResponse { StatusCode = "500", TenantId = "", Message = IdentityValidationMessage.IDENTITY_INSERT_ERROR_MESSAGE });
        }
        private const string StorePendingActivationMessage = "Store is pending activation by super admin.";

        private const string TenantPendingApprovalMessage = "Your account is pending approval by super admin.";

        private const string TenantRejectedMessage = "Your account registration was rejected. Please contact the administrator.";

        /// <summary>
        /// Returns the subset of <paramref name="storeCodes"/> that are activated for the tenant.
        /// Store codes without a status row are treated as active so stores created before the
        /// activation workflow was introduced keep working.
        /// </summary>
        private async Task<List<string>> FilterActiveStoreCodesAsync(string tenantId, List<string> storeCodes)
        {
            if (storeCodes == null || storeCodes.Count == 0)
            {
                return new List<string>();
            }

            var statuses = await _unitOfWork.DbContext.TenantStoreStatuses
                .Where(x => x.TenantId == tenantId)
                .ToListAsync();

            return storeCodes
                .Where(code =>
                {
                    var status = statuses.FirstOrDefault(s => string.Equals(s.StoreCode, code, StringComparison.OrdinalIgnoreCase));
                    return status == null || status.IsActive;
                })
                .ToList();
        }

        public async Task<ActionReturnType> TenantLogin(string eMail, string password, string storeCode = null)
        {
            if (!string.IsNullOrEmpty(eMail))
            {
                // Log runtime DB target and incoming request values to help diagnose "not found" issues.
                try
                {
                    var conn = _unitOfWork.DbContext.Database.GetDbConnection();
                    _logger?.LogInformation("TenantLogin request - DB: {DataSource}/{Database}, Email: {Email}, StoreCode: {StoreCode}", conn.DataSource, conn.Database, eMail, storeCode);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to read DB connection info inside TenantLogin");
                }
                // Super admins are global accounts that are not linked to a tenant or a store.
                // They are resolved first so tenant lookups never shadow them.
                var superAdminEmail = eMail.Trim();
                var normalizedSuperAdminEmail = superAdminEmail.ToLower();
                var superAdmin = await _unitOfWork.DbContext.SuperAdmins
                    .FirstOrDefaultAsync(sa => sa.IsActive
                        && sa.Email != null
                        && sa.Email.ToLower() == normalizedSuperAdminEmail);

                if (superAdmin != null
                    && PasswordHasher.Verify(superAdmin.Password, password, out var superAdminNeedsUpgrade))
                {
                    if (superAdminNeedsUpgrade)
                    {
                        superAdmin.Password = PasswordHasher.Hash(password);
                    }

                    superAdmin.LastLoginDate = DateTime.UtcNow;
                    await _unitOfWork.SaveChangesAsync();
                    // Preserve existing selection of message based on whether the super admin has changed their password.
                    var superAdminMessage = superAdmin.IsPasswordChanged
                        ? IdentityValidationMessage.IDENTITY_DATAFOUND_LOGIN_MESSAGE
                        : IdentityValidationMessage.IDENTITY_DATAFOUND_PASSWORDCHANGE_MESSAGE;

                    return ActionSet.ActionReturnType(HttpStatusCode.OK, new IOResponse
                    {
                        StatusCode = "200",
                        Email = superAdmin.Email,
                        Storecodes = new List<string>(),
                        Message = superAdminMessage,
                        UserRole = "SuperAdmin",
                        MustChangePassword = superAdmin.IsPasswordChanged ? (bool?)false : true
                    });
                }

                // If a storeCode was supplied, prefer authenticating as a store user first
                // so callers explicitly targeting a store don't get shadowed by a tenant row
                // that happens to use the same email address.
                var normalizedEmailForStore = eMail.Trim();
                var normalizedStoreCodeForStore = string.IsNullOrWhiteSpace(storeCode) ? string.Empty : storeCode.Trim();

                if (!string.IsNullOrWhiteSpace(normalizedStoreCodeForStore))
                {
                    var storeUsersQueryForStore = _unitOfWork.DbContext.StoreUsers
                        .Where(su => su.Email == normalizedEmailForStore && su.IsActive && su.StoreCode == normalizedStoreCodeForStore);

                    var candidateStoreUsersForStore = await storeUsersQueryForStore.ToListAsync();

                    var matchedStoreUsersForStore = new List<StoreUserEntity>();
                    var storeUsersNeedingUpgradeForStore = new List<StoreUserEntity>();
                    foreach (var candidate in candidateStoreUsersForStore)
                    {
                        if (PasswordHasher.Verify(candidate.Password, password, out var storeUserNeedsUpgrade))
                        {
                            matchedStoreUsersForStore.Add(candidate);
                            if (storeUserNeedsUpgrade)
                            {
                                storeUsersNeedingUpgradeForStore.Add(candidate);
                            }
                        }
                    }

                    if (matchedStoreUsersForStore.Any())
                    {
                        if (storeUsersNeedingUpgradeForStore.Any())
                        {
                            var upgradedHash = PasswordHasher.Hash(password);
                            foreach (var storeUser in storeUsersNeedingUpgradeForStore)
                            {
                                storeUser.Password = upgradedHash;
                            }

                            await _unitOfWork.SaveChangesAsync();
                        }

                        var primaryStoreUser = matchedStoreUsersForStore.First();
                        var tenantForStoreUser = await _unitOfWork.DbContext.Tenants.FirstOrDefaultAsync(t => t.TenantId == primaryStoreUser.TenantId);

                        // Store users inherit their tenant's approval state.
                        if (tenantForStoreUser != null && !TenantApprovalStatus.IsApproved(tenantForStoreUser.ApprovalStatus))
                        {
                            var storeUserTenantRejected = string.Equals(tenantForStoreUser.ApprovalStatus, TenantApprovalStatus.Rejected, StringComparison.OrdinalIgnoreCase);

                            return ActionSet.ActionReturnType(HttpStatusCode.Forbidden, new IOResponse
                            {
                                StatusCode = "403",
                                Email = eMail,
                                Storecodes = new List<string>(),
                                Message = storeUserTenantRejected ? TenantRejectedMessage : TenantPendingApprovalMessage
                            });
                        }

                        var matchedStores = matchedStoreUsersForStore
                            .Select(x => x.StoreCode)
                            .Where(x => !string.IsNullOrWhiteSpace(x))
                            .Select(x => x.Trim())
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();

                        matchedStores = await FilterActiveStoreCodesAsync(primaryStoreUser.TenantId, matchedStores);

                        if (matchedStores.Count == 0)
                        {
                            return ActionSet.ActionReturnType(HttpStatusCode.Forbidden, new IOResponse
                            {
                                StatusCode = "403",
                                Email = eMail,
                                Message = StorePendingActivationMessage
                            });
                        }

                        return ActionSet.ActionReturnType(HttpStatusCode.OK, new IOResponse
                        {
                            StatusCode = "200",
                            Email = tenantForStoreUser?.Email ?? eMail,
                            Storecodes = matchedStores,
                            Message = IdentityValidationMessage.IDENTITY_DATAFOUND_LOGIN_MESSAGE,
                            UserRole = string.IsNullOrWhiteSpace(primaryStoreUser.Role) ? "StoreUser" : primaryStoreUser.Role
                        });
                    }
                    // fall through to tenant flow if no matching store user found
                }

                var tenantregistryresult = await GetTenantByEmailAsync(eMail);
                if (tenantregistryresult != null)
                {
                    string tenantId = tenantregistryresult.TenantId;
                    var tenantstoreinforesult = await GetTenantStoreByTenantIdAsync(tenantId);

                    if (tenantstoreinforesult != null
                        && PasswordHasher.Verify(tenantstoreinforesult.Password, password, out var tenantNeedsUpgrade))
                    {
                        // Credentials are valid, but the tenant itself must be approved by a super
                        // admin before any store can be accessed.
                        if (!TenantApprovalStatus.IsApproved(tenantregistryresult.ApprovalStatus))
                        {
                            var isRejected = string.Equals(tenantregistryresult.ApprovalStatus, TenantApprovalStatus.Rejected, StringComparison.OrdinalIgnoreCase);
                            var approvalMessage = isRejected ? TenantRejectedMessage : TenantPendingApprovalMessage;

                            if (isRejected && !string.IsNullOrWhiteSpace(tenantregistryresult.ApprovalReason))
                            {
                                approvalMessage = $"{approvalMessage} Reason: {tenantregistryresult.ApprovalReason}";
                            }

                            return ActionSet.ActionReturnType(HttpStatusCode.Forbidden, new IOResponse
                            {
                                StatusCode = "403",
                                Email = eMail,
                                Storecodes = new List<string>(),
                                Message = approvalMessage
                            });
                        }

                        if (tenantNeedsUpgrade)
                        {
                            tenantstoreinforesult.Password = PasswordHasher.Hash(password);
                            await _unitOfWork.SaveChangesAsync();
                        }

                        var assignedStoreCodes = (tenantstoreinforesult.Storecodes ?? new List<string>())
                            .Where(x => !string.IsNullOrWhiteSpace(x))
                            .Select(x => x.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();


                        var tenantStoreCodes = await FilterActiveStoreCodesAsync(tenantId, assignedStoreCodes);

                        if (tenantstoreinforesult.IsPasswordChanged == false)
                        {
                            return ActionSet.ActionReturnType(HttpStatusCode.OK, new IOResponse { StatusCode = "200", Email = eMail, Storecodes = tenantStoreCodes, Message = IdentityValidationMessage.IDENTITY_DATAFOUND_PASSWORDCHANGE_MESSAGE, UserRole = "Admin" });
                        }

                        // The tenant password is shared by every store, so a store that is assigned
                        // but still awaiting activation must report the activation reason instead of
                        // falling through and looking like a credential problem.
                        var requestedStoreCode = string.IsNullOrWhiteSpace(storeCode) ? string.Empty : storeCode.Trim();
                        if (!string.IsNullOrWhiteSpace(requestedStoreCode)
                            && assignedStoreCodes.Contains(requestedStoreCode, StringComparer.OrdinalIgnoreCase)
                            && !tenantStoreCodes.Contains(requestedStoreCode, StringComparer.OrdinalIgnoreCase))
                        {
                            return ActionSet.ActionReturnType(HttpStatusCode.Forbidden, new IOResponse { StatusCode = "403", Email = eMail, Storecodes = tenantStoreCodes, Message = StorePendingActivationMessage });
                        }

                        if (tenantStoreCodes.Count == 0)
                        {
                            return ActionSet.ActionReturnType(HttpStatusCode.Forbidden, new IOResponse { StatusCode = "403", Email = eMail, Storecodes = tenantStoreCodes, Message = StorePendingActivationMessage });
                        }

                        return ActionSet.ActionReturnType(HttpStatusCode.OK, new IOResponse { StatusCode = "200", Email = eMail, Storecodes = tenantStoreCodes, Message = IdentityValidationMessage.IDENTITY_DATAFOUND_LOGIN_MESSAGE, UserRole = "Admin" });
                    }
                }

                // Only run the general store-user lookup when the caller did NOT supply a storeCode.
                // When a storeCode was supplied we already attempted store-user auth above and returned
                // if it succeeded. This avoids duplicating the same work.
                if (string.IsNullOrWhiteSpace(storeCode))
                {
                    var normalizedEmail = eMail.Trim();
                    var normalizedStoreCode = string.IsNullOrWhiteSpace(storeCode) ? string.Empty : storeCode.Trim();

                    var storeUsersQuery = _unitOfWork.DbContext.StoreUsers
                        .Where(su => su.Email == normalizedEmail && su.IsActive);

                    if (!string.IsNullOrWhiteSpace(normalizedStoreCode))
                    {
                        storeUsersQuery = storeUsersQuery.Where(su => su.StoreCode == normalizedStoreCode);
                    }

                    var candidateStoreUsers = await storeUsersQuery.ToListAsync();

                    var matchedStoreUsers = new List<StoreUserEntity>();
                    var storeUsersNeedingUpgrade = new List<StoreUserEntity>();
                    _logger?.LogInformation("Found {Count} candidate store users for email {Email}", candidateStoreUsers.Count, normalizedEmail);
                    foreach (var candidate in candidateStoreUsers)
                    {
                        try
                        {
                            var verified = PasswordHasher.Verify(candidate.Password, password, out var storeUserNeedsUpgrade);
                            _logger?.LogInformation("Password verify for StoreUser {Email} (TenantId={TenantId}, StoreCode={StoreCode}) => {Verified}", candidate.Email, candidate.TenantId, candidate.StoreCode, verified);
                            if (verified)
                            {
                                matchedStoreUsers.Add(candidate);
                                if (storeUserNeedsUpgrade)
                                {
                                    storeUsersNeedingUpgrade.Add(candidate);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogWarning(ex, "Password verification failed for StoreUser {Email}", candidate?.Email);
                        }
                    }

                    if (matchedStoreUsers.Any())
                    {
                        if (storeUsersNeedingUpgrade.Any())
                        {
                            var upgradedHash = PasswordHasher.Hash(password);
                            foreach (var storeUser in storeUsersNeedingUpgrade)
                            {
                                storeUser.Password = upgradedHash;
                            }

                            await _unitOfWork.SaveChangesAsync();
                        }

                        var primaryStoreUser = matchedStoreUsers.First();
                        var tenantForStoreUser = await _unitOfWork.DbContext.Tenants.FirstOrDefaultAsync(t => t.TenantId == primaryStoreUser.TenantId);

                        // Store users inherit their tenant's approval state.
                        if (tenantForStoreUser != null && !TenantApprovalStatus.IsApproved(tenantForStoreUser.ApprovalStatus))
                        {
                            var storeUserTenantRejected = string.Equals(tenantForStoreUser.ApprovalStatus, TenantApprovalStatus.Rejected, StringComparison.OrdinalIgnoreCase);

                            return ActionSet.ActionReturnType(HttpStatusCode.Forbidden, new IOResponse
                            {
                                StatusCode = "403",
                                Email = eMail,
                                Storecodes = new List<string>(),
                                Message = storeUserTenantRejected ? TenantRejectedMessage : TenantPendingApprovalMessage
                            });
                        }

                        var matchedStores = matchedStoreUsers
                            .Select(x => x.StoreCode)
                            .Where(x => !string.IsNullOrWhiteSpace(x))
                            .Select(x => x.Trim())
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();

                        matchedStores = await FilterActiveStoreCodesAsync(primaryStoreUser.TenantId, matchedStores);

                        if (matchedStores.Count == 0)
                        {
                            return ActionSet.ActionReturnType(HttpStatusCode.Forbidden, new IOResponse
                            {
                                StatusCode = "403",
                                Email = eMail,
                                Message = StorePendingActivationMessage
                            });
                        }

                        return ActionSet.ActionReturnType(HttpStatusCode.OK, new IOResponse
                        {
                            StatusCode = "200",
                            Email = tenantForStoreUser?.Email ?? eMail,
                            Storecodes = matchedStores,
                            Message = IdentityValidationMessage.IDENTITY_DATAFOUND_LOGIN_MESSAGE,
                            UserRole = string.IsNullOrWhiteSpace(primaryStoreUser.Role) ? "StoreUser" : primaryStoreUser.Role
                        });
                    }
                }

                return ActionSet.ActionReturnType(HttpStatusCode.NotFound, new IOResponse { StatusCode = "200", Email = eMail, Message = IdentityValidationMessage.IDENTITY_DATANOTFOUND_LOGIN_MESSAGE });
            }
            else
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new IOResponse { StatusCode = "200", Email = eMail, Message = IdentityValidationMessage.IDENTITY_TENANT_NOT_FOUND });
            }
        }

        public async Task<ActionReturnType> CreateTenantStore(string tenantEmail, string storeCode)
        {
            try
            {
                return await CreateTenantStoreCore(tenantEmail, storeCode);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create a tenant store.");
                throw;
            }
        }

        private async Task<ActionReturnType> CreateTenantStoreCore(string tenantEmail, string storeCode)
        {
            var normalizedEmail = (tenantEmail ?? string.Empty).Trim();
            var normalizedStoreCode = (storeCode ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(normalizedEmail) || string.IsNullOrWhiteSpace(normalizedStoreCode))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new IOResponse
                {
                    StatusCode = "400",
                    Message = "Tenant email and store code are required."
                });
            }

            var tenant = await GetTenantByEmailAsync(normalizedEmail);
            if (tenant == null)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.NotFound, new IOResponse
                {
                    StatusCode = "404",
                    Message = "Tenant not found."
                });
            }

            var tenantStoreInfo = await GetTenantStoreByTenantIdAsync(tenant.TenantId);
            if (tenantStoreInfo == null)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.NotFound, new IOResponse
                {
                    StatusCode = "404",
                    Message = "Tenant store profile not found."
                });
            }

            var existingStoreCodes = (tenantStoreInfo.Storecodes ?? new List<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (existingStoreCodes.Contains(normalizedStoreCode, StringComparer.OrdinalIgnoreCase))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.Conflict, new IOResponse
                {
                    StatusCode = "409",
                    Message = "Store code already exists.",
                    Storecodes = existingStoreCodes
                });
            }
            existingStoreCodes.Add(normalizedStoreCode);
            tenantStoreInfo.Storecodes = existingStoreCodes;

            var existingStatus = await _unitOfWork.DbContext.TenantStoreStatuses
                .FirstOrDefaultAsync(x => x.TenantId == tenant.TenantId && x.StoreCode == normalizedStoreCode);

            if (existingStatus == null)
            {
                _unitOfWork.DbContext.TenantStoreStatuses.Add(new TenantStoreStatusEntity
                {
                    TenantId = tenant.TenantId,
                    StoreCode = normalizedStoreCode,
                    IsActive = false,
                    CreatedDate = DateTime.UtcNow
                });
            }

            // perform changes in a single transaction so tenant storeinfo, statuses and configuration are consistent
            using (var trx = await _unitOfWork.DbContext.Database.BeginTransactionAsync())
            {
                try
                {
                    // Resolve currency: priority -> tenantStoreInfo columns -> tenant-level config -> country defaults
                    string currencyCode = null;
                    string currencySymbol = null;
                    string currencyLocale = null;
                    int? currencyDigits = null;

                    if (!string.IsNullOrWhiteSpace(tenantStoreInfo?.CurrencyCode)) currencyCode = tenantStoreInfo.CurrencyCode;
                    if (!string.IsNullOrWhiteSpace(tenantStoreInfo?.CurrencySymbol)) currencySymbol = tenantStoreInfo.CurrencySymbol;
                    if (!string.IsNullOrWhiteSpace(tenantStoreInfo?.CurrencyLocale)) currencyLocale = tenantStoreInfo.CurrencyLocale;
                    if (tenantStoreInfo?.CurrencyDecimalDigits.HasValue == true) currencyDigits = tenantStoreInfo.CurrencyDecimalDigits;

                    var tenantLevelCfg = await _unitOfWork.DbContext.StoreConfigurations
                        .FirstOrDefaultAsync(x => x.TenantId == tenant.TenantId && (x.StoreCode == null || x.StoreCode == string.Empty));

                    if (tenantLevelCfg != null)
                    {
                        currencyCode = currencyCode ?? tenantLevelCfg.CurrencyCode;
                        currencySymbol = currencySymbol ?? tenantLevelCfg.CurrencySymbol;
                        currencyLocale = currencyLocale ?? tenantLevelCfg.CurrencyLocale;
                        currencyDigits = currencyDigits ?? tenantLevelCfg.CurrencyDecimalDigits;
                    }

                    if (string.IsNullOrWhiteSpace(currencyCode) || string.IsNullOrWhiteSpace(currencySymbol) || string.IsNullOrWhiteSpace(currencyLocale) || currencyDigits == null)
                    {
                        var defaultCfg = CountryCurrencyMapping.GetDefaultForCountry(tenant.Country);
                        if (defaultCfg != null)
                        {
                            currencyCode = currencyCode ?? defaultCfg.CurrencyCode;
                            currencySymbol = currencySymbol ?? defaultCfg.CurrencySymbol;
                            currencyLocale = currencyLocale ?? defaultCfg.CurrencyLocale;
                            currencyDigits = currencyDigits ?? defaultCfg.DecimalDigits;
                        }
                    }

                    // Persist resolved currency to tenant store info so it is available for future store creations
                    if (tenantStoreInfo != null)
                    {
                        tenantStoreInfo.CurrencyCode = currencyCode;
                        tenantStoreInfo.CurrencySymbol = currencySymbol;
                        tenantStoreInfo.CurrencyLocale = currencyLocale;
                        tenantStoreInfo.CurrencyDecimalDigits = currencyDigits;
                    }

                    // Ensure store configuration exists for the new store and contains comprehensive JSON
                    var existingCfg = await _unitOfWork.DbContext.StoreConfigurations
                        .FirstOrDefaultAsync(x => x.TenantId == tenant.TenantId && x.StoreCode == normalizedStoreCode);

                    if (existingCfg == null)
                    {
                        string configurationJson = tenantLevelCfg?.ConfigurationJson ?? Newtonsoft.Json.JsonConvert.SerializeObject(new { TenantName = tenant.Email, StoreCode = normalizedStoreCode });

                        try
                        {
                            var j = JObject.Parse(configurationJson ?? string.Empty);
                            j["TenantName"] = tenant.Email;
                            j["StoreCode"] = normalizedStoreCode;
                            if (!string.IsNullOrWhiteSpace(currencyCode)) j["CurrencyCode"] = currencyCode;
                            if (!string.IsNullOrWhiteSpace(currencySymbol)) j["CurrencySymbol"] = currencySymbol;
                            if (!string.IsNullOrWhiteSpace(currencyLocale)) j["CurrencyLocale"] = currencyLocale;
                            if (currencyDigits.HasValue) j["CurrencyDecimalDigits"] = currencyDigits.Value;
                            configurationJson = j.ToString(Newtonsoft.Json.Formatting.None);
                        }
                        catch
                        {
                            configurationJson = Newtonsoft.Json.JsonConvert.SerializeObject(new
                            {
                                TenantName = tenant.Email,
                                StoreCode = normalizedStoreCode,
                                CurrencyCode = currencyCode,
                                CurrencySymbol = currencySymbol,
                                CurrencyLocale = currencyLocale,
                                CurrencyDecimalDigits = currencyDigits
                            });
                        }

                        var cfgEntity = new StoreConfigurationEntity
                        {
                            TenantId = tenant.TenantId,
                            StoreCode = normalizedStoreCode,
                            ConfigurationJson = configurationJson,
                            CurrencyCode = string.IsNullOrWhiteSpace(currencyCode) ? null : currencyCode,
                            CurrencySymbol = string.IsNullOrWhiteSpace(currencySymbol) ? null : currencySymbol,
                            CurrencyLocale = string.IsNullOrWhiteSpace(currencyLocale) ? null : currencyLocale,
                            CurrencyDecimalDigits = currencyDigits,
                            CreatedDate = DateTime.UtcNow,
                            CreatedBy = tenant.Email,
                            ModifiedDate = DateTime.UtcNow,
                            ModifiedBy = tenant.Email
                        };

                        _unitOfWork.DbContext.StoreConfigurations.Add(cfgEntity);
                    }

                    await _unitOfWork.SaveChangesAsync();
                    await trx.CommitAsync();
                }
                catch (Exception ex)
                {
                    await trx.RollbackAsync();
                    _logger?.LogError(ex, "Failed while creating store {StoreCode} for TenantId {TenantId}", normalizedStoreCode, tenant.TenantId);
                    throw;
                }
            }

            return ActionSet.ActionReturnType(HttpStatusCode.OK, new IOResponse
            {
                StatusCode = "200",
                Message = "Store created successfully. It is inactive until a super admin activates it.",
                Storecodes = existingStoreCodes
            });
        }

        public async Task<ActionReturnType> GetTenantStoreStatuses(string tenantEmail)
        {
            var tenant = await GetTenantByEmailAsync((tenantEmail ?? string.Empty).Trim());
            if (tenant == null)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.NotFound, new IOResponse
                {
                    StatusCode = "404",
                    Message = "Tenant not found.",
                    StoreStatuses = new List<TenantStoreStatusDto>()
                });
            }

            var tenantStoreInfo = await GetTenantStoreByTenantIdAsync(tenant.TenantId);
            var storeCodes = (tenantStoreInfo?.Storecodes ?? new List<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var statuses = await _unitOfWork.DbContext.TenantStoreStatuses
                .Where(x => x.TenantId == tenant.TenantId)
                .ToListAsync();

            var result = storeCodes
                .Select(code =>
                {
                    var status = statuses.FirstOrDefault(s => string.Equals(s.StoreCode, code, StringComparison.OrdinalIgnoreCase));
                    return new TenantStoreStatusDto
                    {
                        StoreCode = code,
                        // Treat missing status as inactive: newly created stores are inactive until a super admin activates them.
                        IsActive = status?.IsActive ?? false,
                        ActivatedBy = status?.ActivatedBy,
                        ActivatedDate = status?.ActivatedDate
                    };
                })
                .ToList();

            return ActionSet.ActionReturnType(HttpStatusCode.OK, new IOResponse
            {
                StatusCode = "200",
                Message = "Store statuses fetched successfully.",
                Storecodes = storeCodes,
                StoreStatuses = result
            });
        }

        /// <summary>
        /// Returns the activation state of every store across all tenants. Used by the
        /// super admin approval screen, which is not scoped to a single tenant.
        /// </summary>
        public async Task<ActionReturnType> GetAllStoreStatuses()
        {
            var tenants = await _unitOfWork.DbContext.Tenants.ToListAsync();
            var tenantStoreInfos = await _unitOfWork.DbContext.TenantStoreInfos.ToListAsync();
            var statuses = await _unitOfWork.DbContext.TenantStoreStatuses.ToListAsync();

            var result = new List<TenantStoreStatusDto>();

            foreach (var tenant in tenants)
            {
                var storeInfo = tenantStoreInfos.FirstOrDefault(x => x.TenantId == tenant.TenantId);
                var storeCodes = (storeInfo?.Storecodes ?? new List<string>())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var code in storeCodes)
                {
                    var status = statuses.FirstOrDefault(s => s.TenantId == tenant.TenantId
                        && string.Equals(s.StoreCode, code, StringComparison.OrdinalIgnoreCase));

                    result.Add(new TenantStoreStatusDto
                    {
                        StoreCode = code,
                        IsActive = status == null || status.IsActive,
                        ActivatedBy = status?.ActivatedBy,
                        ActivatedDate = status?.ActivatedDate,
                        TenantId = tenant.TenantId,
                        TenantName = tenant.TenantName,
                        TenantEmail = tenant.Email
                    });
                }
            }

            result = result
                .OrderBy(x => x.IsActive)
                .ThenBy(x => x.TenantName)
                .ThenBy(x => x.StoreCode)
                .ToList();

            return ActionSet.ActionReturnType(HttpStatusCode.OK, new IOResponse
            {
                StatusCode = "200",
                Message = "Store statuses fetched successfully.",
                StoreStatuses = result
            });
        }

        public async Task<ActionReturnType> SetTenantStoreActiveStatus(string tenantEmail, string storeCode, bool isActive, string activatedBy)
        {
            var normalizedStoreCode = (storeCode ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(tenantEmail) || string.IsNullOrWhiteSpace(normalizedStoreCode))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new IOResponse
                {
                    StatusCode = "400",
                    Message = "Tenant email and store code are required."
                });
            }

            var tenant = await GetTenantByEmailAsync(tenantEmail.Trim());
            if (tenant == null)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.NotFound, new IOResponse
                {
                    StatusCode = "404",
                    Message = "Tenant not found."
                });
            }

            var tenantStoreInfo = await GetTenantStoreByTenantIdAsync(tenant.TenantId);
            var belongsToTenant = (tenantStoreInfo?.Storecodes ?? new List<string>())
                .Any(x => string.Equals((x ?? string.Empty).Trim(), normalizedStoreCode, StringComparison.OrdinalIgnoreCase));

            if (!belongsToTenant)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new IOResponse
                {
                    StatusCode = "400",
                    Message = "Store code does not belong to tenant."
                });
            }

            var status = await _unitOfWork.DbContext.TenantStoreStatuses
                .FirstOrDefaultAsync(x => x.TenantId == tenant.TenantId && x.StoreCode == normalizedStoreCode);

            if (status == null)
            {
                status = new TenantStoreStatusEntity
                {
                    TenantId = tenant.TenantId,
                    StoreCode = normalizedStoreCode,
                    CreatedDate = DateTime.UtcNow
                };

                _unitOfWork.DbContext.TenantStoreStatuses.Add(status);
            }

            status.IsActive = isActive;
            status.ActivatedBy = activatedBy;
            status.ActivatedDate = DateTime.UtcNow;

            await _unitOfWork.SaveChangesAsync();

            return ActionSet.ActionReturnType(HttpStatusCode.OK, new IOResponse
            {
                StatusCode = "200",
                Message = isActive
                    ? $"Store {normalizedStoreCode} activated successfully."
                    : $"Store {normalizedStoreCode} deactivated successfully."
            });
        }

        /// <summary>
        /// Activates or deactivates a store without knowing its tenant up front. The owning
        /// tenant is resolved from the store code so super admins can approve any store.
        /// </summary>
        public async Task<ActionReturnType> SetStoreActiveStatusByStoreCode(string storeCode, bool isActive, string activatedBy)
        {
            var normalizedStoreCode = (storeCode ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedStoreCode))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new IOResponse
                {
                    StatusCode = "400",
                    Message = "Store code is required."
                });
            }

            var owningTenantIds = (await _unitOfWork.DbContext.TenantStoreInfos.ToListAsync())
                .Where(info => (info.Storecodes ?? new List<string>())
                    .Any(x => string.Equals((x ?? string.Empty).Trim(), normalizedStoreCode, StringComparison.OrdinalIgnoreCase)))
                .Select(info => info.TenantId)
                .Distinct()
                .ToList();

            if (owningTenantIds.Count == 0)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.NotFound, new IOResponse
                {
                    StatusCode = "404",
                    Message = $"Store {normalizedStoreCode} was not found."
                });
            }

            if (owningTenantIds.Count > 1)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.Conflict, new IOResponse
                {
                    StatusCode = "409",
                    Message = $"Store {normalizedStoreCode} is mapped to multiple tenants."
                });
            }

            var tenantId = owningTenantIds[0];
            var status = await _unitOfWork.DbContext.TenantStoreStatuses
                .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.StoreCode == normalizedStoreCode);

            if (status == null)
            {
                status = new TenantStoreStatusEntity
                {
                    TenantId = tenantId,
                    StoreCode = normalizedStoreCode,
                    CreatedDate = DateTime.UtcNow
                };

                _unitOfWork.DbContext.TenantStoreStatuses.Add(status);
            }

            status.IsActive = isActive;
            status.ActivatedBy = activatedBy;
            status.ActivatedDate = DateTime.UtcNow;

            await _unitOfWork.SaveChangesAsync();

            return ActionSet.ActionReturnType(HttpStatusCode.OK, new IOResponse
            {
                StatusCode = "200",
                Message = isActive
                    ? $"Store {normalizedStoreCode} activated successfully."
                    : $"Store {normalizedStoreCode} deactivated successfully."
            });
        }

        /// <summary>
        /// Returns the approval state of every tenant for the super admin approval screen.
        /// </summary>
        public async Task<ActionReturnType> GetAllTenantApprovals()
        {
            var tenants = await _unitOfWork.DbContext.Tenants.ToListAsync();
            var tenantStoreInfos = await _unitOfWork.DbContext.TenantStoreInfos.ToListAsync();

            var result = tenants.Select(tenant =>
            {
                var storeInfo = tenantStoreInfos.FirstOrDefault(x => x.TenantId == tenant.TenantId);
                var storeCount = (storeInfo?.Storecodes ?? new List<string>())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();

                return new TenantApprovalDto
                {
                    TenantId = tenant.TenantId,
                    TenantName = tenant.TenantName,
                    Email = tenant.Email,
                    PhoneNumber = tenant.PhoneNumber,
                    City = tenant.City,
                    Country = tenant.Country,
                    ApprovalStatus = TenantApprovalStatus.Normalize(tenant.ApprovalStatus),
                    ApprovalReason = tenant.ApprovalReason,
                    ApprovedBy = tenant.ApprovedBy,
                    ApprovalDate = tenant.ApprovalDate,
                    CreatedDate = tenant.CreatedDate,
                    StoreCount = storeCount
                };
            })
            // Pending tenants first so the super admin sees outstanding work at the top.
            .OrderBy(x => x.ApprovalStatus == TenantApprovalStatus.Pending ? 0 : 1)
            .ThenBy(x => x.TenantName)
            .ToList();

            return ActionSet.ActionReturnType(HttpStatusCode.OK, new IOResponse
            {
                StatusCode = "200",
                Message = "Tenant approvals fetched successfully.",
                TenantApprovals = result
            });
        }

        /// <summary>
        /// Approves or rejects a tenant. A reason is required when rejecting so the
        /// decision can be communicated back to the tenant.
        /// </summary>
        public async Task<ActionReturnType> SetTenantApprovalStatus(string tenantId, string approvalStatus, string reason, string actionedBy)
        {
            var normalizedTenantId = (tenantId ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedTenantId))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new IOResponse
                {
                    StatusCode = "400",
                    Message = "Tenant id is required."
                });
            }

            var requestedStatus = (approvalStatus ?? string.Empty).Trim();
            var isApprove = string.Equals(requestedStatus, TenantApprovalStatus.Approved, StringComparison.OrdinalIgnoreCase);
            var isReject = string.Equals(requestedStatus, TenantApprovalStatus.Rejected, StringComparison.OrdinalIgnoreCase);
            var isPending = string.Equals(requestedStatus, TenantApprovalStatus.Pending, StringComparison.OrdinalIgnoreCase);

            if (!isApprove && !isReject && !isPending)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new IOResponse
                {
                    StatusCode = "400",
                    Message = "Approval status must be Approved, Rejected or Pending."
                });
            }

            if (isReject && string.IsNullOrWhiteSpace(reason))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new IOResponse
                {
                    StatusCode = "400",
                    Message = "A reason is required when rejecting a tenant."
                });
            }

            var tenant = await _unitOfWork.DbContext.Tenants.FirstOrDefaultAsync(x => x.TenantId == normalizedTenantId);
            if (tenant == null)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.NotFound, new IOResponse
                {
                    StatusCode = "404",
                    Message = $"Tenant {normalizedTenantId} was not found."
                });
            }

            tenant.ApprovalStatus = isApprove
                ? TenantApprovalStatus.Approved
                : isReject ? TenantApprovalStatus.Rejected : TenantApprovalStatus.Pending;
            tenant.ApprovalReason = isReject ? reason.Trim() : null;
            tenant.ApprovedBy = actionedBy;
            tenant.ApprovalDate = DateTime.UtcNow;
            tenant.ModifiedDate = DateTime.UtcNow;

            // Also update the legacy TenantStoreInfos.Status to reflect approval state
            // so UI and authentication checks (which read TenantStoreInfos.Status) see the
            // correct active/inactive value. Do not modify other fields.
            var tenantStoreInfo = await GetTenantStoreByTenantIdAsync(tenant.TenantId);
            if (tenantStoreInfo != null)
            {
                tenantStoreInfo.Status = isApprove ? "Active" : (isReject ? "InActive" : tenantStoreInfo.Status);
            }

            await _unitOfWork.SaveChangesAsync();

            var message = isApprove
                ? $"Tenant {tenant.TenantName} approved successfully."
                : isReject
                    ? $"Tenant {tenant.TenantName} rejected successfully."
                    : $"Tenant {tenant.TenantName} moved back to pending.";

            return ActionSet.ActionReturnType(HttpStatusCode.OK, new IOResponse
            {
                StatusCode = "200",
                Message = message
            });
        }

        public async Task<ActionReturnType> CreateStoreUser(StoreUserDto model)
        {
            if (model == null || string.IsNullOrWhiteSpace(model.TenantEmail) || string.IsNullOrWhiteSpace(model.StoreCode)
                || string.IsNullOrWhiteSpace(model.Email) || string.IsNullOrWhiteSpace(model.Password))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new IOResponse
                {
                    StatusCode = "400",
                    Message = "Tenant email, store code, user email and password are required."
                });
            }

            var tenant = await GetTenantByEmailAsync(model.TenantEmail.Trim());
            if (tenant == null)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.NotFound, new IOResponse
                {
                    StatusCode = "404",
                    Message = "Tenant not found."
                });
            }

            var tenantStore = await GetTenantStoreByTenantIdAsync(tenant.TenantId);
            var storeCode = model.StoreCode.Trim();
            var normalizedEmail = model.Email.Trim();
            if (tenantStore == null || tenantStore.Storecodes == null || !tenantStore.Storecodes.Any(s => string.Equals(s, storeCode, StringComparison.OrdinalIgnoreCase)))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new IOResponse
                {
                    StatusCode = "400",
                    Message = "Store code does not belong to tenant."
                });
            }

            var existing = await _unitOfWork.DbContext.StoreUsers.FirstOrDefaultAsync(x => x.TenantId == tenant.TenantId && x.StoreCode == storeCode && x.Email == normalizedEmail);
            if (existing != null)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.Conflict, new IOResponse
                {
                    StatusCode = "409",
                    Message = "Store user already exists for this store."
                });
            }

            var entity = new StoreUserEntity
            {
                TenantId = tenant.TenantId,
                StoreCode = storeCode,
                Email = normalizedEmail,
                Password = PasswordHasher.Hash(model.Password),
                Role = string.IsNullOrWhiteSpace(model.Role) ? "StoreUser" : model.Role.Trim(),
                IsActive = model.IsActive,
                CreatedDate = DateTime.UtcNow,
                CreatedBy = string.IsNullOrWhiteSpace(model.CreatedBy) ? model.TenantEmail : model.CreatedBy.Trim()
            };

            _unitOfWork.DbContext.StoreUsers.Add(entity);
            await _unitOfWork.SaveChangesAsync();

            return ActionSet.ActionReturnType(HttpStatusCode.OK, new IOResponse
            {
                StatusCode = "200",
                Message = "Store user created successfully."
            });
        }

        public async Task<ActionReturnType> GetStoreUsers(string tenantEmail, string storeCode)
        {
            if (string.IsNullOrWhiteSpace(tenantEmail) || string.IsNullOrWhiteSpace(storeCode))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new IOResponse
                {
                    StatusCode = "400",
                    Message = "Tenant email and store code are required."
                });
            }

            var tenant = await GetTenantByEmailAsync(tenantEmail.Trim());
            if (tenant == null)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.NotFound, new IOResponse
                {
                    StatusCode = "404",
                    Message = "Tenant not found."
                });
            }

            var users = await _unitOfWork.DbContext.StoreUsers
                .Where(x => x.TenantId == tenant.TenantId && x.StoreCode == storeCode)
                .Select(x => new
                {
                    x.Email,
                    x.Role,
                    x.IsActive,
                    x.CreatedDate
                })
                .ToListAsync();

            return ActionSet.ActionReturnType(HttpStatusCode.OK, new
            {
                StatusCode = "200",
                Message = string.Empty,
                Users = users
            });
        }

        public async Task<ActionReturnType> SetStoreUserActiveStatus(string tenantEmail, string storeCode, string userEmail, bool isActive)
        {
            if (string.IsNullOrWhiteSpace(tenantEmail)
                || string.IsNullOrWhiteSpace(storeCode)
                || string.IsNullOrWhiteSpace(userEmail))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new IOResponse
                {
                    StatusCode = "400",
                    Message = "Tenant email, store code and user email are required."
                });
            }

            var tenant = await GetTenantByEmailAsync(tenantEmail.Trim());
            if (tenant == null)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.NotFound, new IOResponse
                {
                    StatusCode = "404",
                    Message = "Tenant not found."
                });
            }

            var normalizedStoreCode = storeCode.Trim();
            var normalizedUserEmail = userEmail.Trim();

            var existingUser = await _unitOfWork.DbContext.StoreUsers.FirstOrDefaultAsync(x =>
                x.TenantId == tenant.TenantId
                && x.StoreCode == normalizedStoreCode
                && x.Email == normalizedUserEmail);

            if (existingUser == null)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.NotFound, new IOResponse
                {
                    StatusCode = "404",
                    Message = "Store user not found."
                });
            }

            if (existingUser.IsActive == isActive)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.OK, new IOResponse
                {
                    StatusCode = "200",
                    Message = isActive ? "Store user is already active." : "Store user is already inactive."
                });
            }

            existingUser.IsActive = isActive;
            await _unitOfWork.SaveChangesAsync();

            return ActionSet.ActionReturnType(HttpStatusCode.OK, new IOResponse
            {
                StatusCode = "200",
                Message = isActive ? "Store user activated successfully." : "Store user deactivated successfully."
            });
        }

        public async Task<ActionReturnType> GetStoreConfiguration(string tenantEmail, string storeCode)
        {
            // tenantEmail is required; storeCode may be empty string to indicate tenant-level configuration
            if (string.IsNullOrWhiteSpace(tenantEmail) || storeCode == null)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new IOResponse
                {
                    StatusCode = "400",
                    Message = "Tenant email is required. Store code may be empty for tenant-level configuration."
                });
            }

            var tenant = await GetTenantByEmailAsync(tenantEmail.Trim());
            if (tenant == null)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.NotFound, new IOResponse
                {
                    StatusCode = "404",
                    Message = "Tenant not found."
                });
            }

            var normalizedStoreCode = storeCode.Trim();
            var config = await _unitOfWork.DbContext.StoreConfigurations.FirstOrDefaultAsync(x =>
                x.TenantId == tenant.TenantId && x.StoreCode == normalizedStoreCode);

            if (config == null)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.NotFound, new IOResponse
                {
                    StatusCode = "404",
                    Email = tenantEmail,
                    Message = "Store configuration not found."
                });
            }

            return ActionSet.ActionReturnType(HttpStatusCode.OK, new IOResponse
            {
                StatusCode = "200",
                Email = tenantEmail,
                Storecodes = new List<string> { normalizedStoreCode },
                // Prefer dedicated currency columns as authoritative at runtime; fall back to values embedded in JSON for legacy rows.
                ConfigurationJson = BuildConfigurationJsonWithCurrencyOverrides(config),
                Message = string.Empty
            });
        }

        // Merge dedicated currency columns into ConfigurationJson for runtime consumers.
        private static string BuildConfigurationJsonWithCurrencyOverrides(StoreConfigurationEntity config)
        {
            try
            {
                var j = string.IsNullOrWhiteSpace(config.ConfigurationJson)
                    ? new JObject()
                    : JObject.Parse(config.ConfigurationJson);

                if (!string.IsNullOrWhiteSpace(config.CurrencyCode)) j["CurrencyCode"] = config.CurrencyCode;
                if (!string.IsNullOrWhiteSpace(config.CurrencySymbol)) j["CurrencySymbol"] = config.CurrencySymbol;
                if (!string.IsNullOrWhiteSpace(config.CurrencyLocale)) j["CurrencyLocale"] = config.CurrencyLocale;
                if (config.CurrencyDecimalDigits.HasValue) j["CurrencyDecimalDigits"] = config.CurrencyDecimalDigits.Value;

                return j.ToString(Newtonsoft.Json.Formatting.None);
            }
            catch
            {
                // On parse error, fall back to raw JSON to avoid breaking API contract.
                return config.ConfigurationJson ?? string.Empty;
            }
        }

        public async Task<ActionReturnType> NewStoreConfiguration(NewStoreConfigurationDto model)
        {
            // Allow storeCode to be empty string for tenant-level configuration; require tenant email and configuration JSON
            if (model == null || string.IsNullOrWhiteSpace(model.TenantEmail) || model.StoreCode == null || string.IsNullOrWhiteSpace(model.ConfigurationJson))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new IOResponse
                {
                    StatusCode = "400",
                    Message = "Tenant email and configuration are required. Store code may be empty for tenant-level configuration."
                });
            }

            var normalizedTenantEmail = model.TenantEmail.Trim();
            var normalizedStoreCode = model.StoreCode.Trim();
            var tenant = await GetTenantByEmailAsync(normalizedTenantEmail);
            if (tenant == null)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.NotFound, new IOResponse
                {
                    StatusCode = "404",
                    Message = "Tenant not found."
                });
            }

            var utcNow = DateTime.UtcNow;
            var updatedBy = string.IsNullOrWhiteSpace(model.UpdatedBy) ? normalizedTenantEmail : model.UpdatedBy.Trim();

            var existing = await _unitOfWork.DbContext.StoreConfigurations.FirstOrDefaultAsync(x =>
                x.TenantId == tenant.TenantId && x.StoreCode == normalizedStoreCode);

            if (existing == null)
            {
                existing = new StoreConfigurationEntity
                {
                    TenantId = tenant.TenantId,
                    StoreCode = normalizedStoreCode,
                    ConfigurationJson = model.ConfigurationJson,
                    // Extract currency values from supplied JSON (best-effort). Missing values remain null.
                    CurrencyCode = null,
                    CurrencySymbol = null,
                    CurrencyLocale = null,
                    CurrencyDecimalDigits = null,
                    CreatedDate = utcNow,
                    CreatedBy = updatedBy,
                    ModifiedDate = utcNow,
                    ModifiedBy = updatedBy
                };

                // parse JSON for currency override
                try
                {
                    var j = JObject.Parse(model.ConfigurationJson ?? string.Empty);
                    var cc = j["CurrencyCode"]?.ToString(); if (!string.IsNullOrWhiteSpace(cc)) existing.CurrencyCode = cc;
                    var cs = j["CurrencySymbol"]?.ToString(); if (!string.IsNullOrWhiteSpace(cs)) existing.CurrencySymbol = cs;
                    var cl = j["CurrencyLocale"]?.ToString(); if (!string.IsNullOrWhiteSpace(cl)) existing.CurrencyLocale = cl;
                    if (j["CurrencyDecimalDigits"] != null && int.TryParse(j["CurrencyDecimalDigits"].ToString(), out var digs)) existing.CurrencyDecimalDigits = digs;
                }
                catch { }

                _unitOfWork.DbContext.StoreConfigurations.Add(existing);
            }
            else
            {
                existing.ConfigurationJson = model.ConfigurationJson;
                // update dedicated currency columns if present in the incoming JSON
                try
                {
                    var j = JObject.Parse(model.ConfigurationJson ?? string.Empty);
                    var cc = j["CurrencyCode"]?.ToString(); if (!string.IsNullOrWhiteSpace(cc)) existing.CurrencyCode = cc;
                    var cs = j["CurrencySymbol"]?.ToString(); if (!string.IsNullOrWhiteSpace(cs)) existing.CurrencySymbol = cs;
                    var cl = j["CurrencyLocale"]?.ToString(); if (!string.IsNullOrWhiteSpace(cl)) existing.CurrencyLocale = cl;
                    if (j["CurrencyDecimalDigits"] != null && int.TryParse(j["CurrencyDecimalDigits"].ToString(), out var digs)) existing.CurrencyDecimalDigits = digs;
                }
                catch { }
                existing.ModifiedDate = utcNow;
                existing.ModifiedBy = updatedBy;
            }

            await _unitOfWork.SaveChangesAsync();

            return ActionSet.ActionReturnType(HttpStatusCode.OK, new IOResponse
            {
                StatusCode = "200",
                Email = normalizedTenantEmail,
                Storecodes = new List<string> { normalizedStoreCode },
                ConfigurationJson = model.ConfigurationJson,
                Message = "Store configuration saved successfully."
            });
        }

        public async Task<ActionReturnType> ForgotPassword(string eMail)
        {
            if (!string.IsNullOrEmpty(eMail))
            {
                var tenantregistryresult = await GetTenantByEmailAsync(eMail);
                if (tenantregistryresult == null)
                    return ActionSet.ActionReturnType(HttpStatusCode.NotFound, new IOResponse { Email = eMail, Message = IdentityValidationMessage.IDENTITY_DATANOTFOUND_LOGIN_MESSAGE });
                string tenantId = tenantregistryresult.TenantId;
                var tenantstoreinforesult = await GetTenantStoreByTenantIdAsync(tenantId);
                if (tenantstoreinforesult != null)
                {
                    string password = string.Empty;
                    password = Utility.encode(eMail, 8);

                    tenantstoreinforesult.IsPasswordChanged = false;
                    tenantstoreinforesult.Password = PasswordHasher.Hash(password);
                    await _unitOfWork.SaveChangesAsync();
                     await SendEmailToChangePasswordAsync(eMail, password);

                    return ActionSet.ActionReturnType(HttpStatusCode.OK, new IOResponse { Email = eMail, Message = IdentityValidationMessage.IDENTITY_DATAFOUND_CHANGEPASSWORDSUCCESS_MESSAGE });
                }
                else
                {
                    return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new IOResponse { Email = eMail, Message = IdentityValidationMessage.IDENTITY_TENANT_NOT_FOUND });
                }
            }
            else
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new IOResponse { Email = eMail, Message = IdentityValidationMessage.IDENTITY_TENANT_NOT_FOUND });
            }
        }

        public async Task<ActionReturnType> TenantProfileDetails(string eMail)
        {
            if (!string.IsNullOrEmpty(eMail))
            {
                var tenantregistryresult = await GetTenantByEmailAsync(eMail);
                if (tenantregistryresult == null)
                    return ActionSet.ActionReturnType(HttpStatusCode.NotFound, new IOResponse { StatusCode = "404", Email = eMail, Message = IdentityValidationMessage.IDENTITY_DATANOTFOUND_EMAIL_MESSAGE });
                else
                    return ActionSet.ActionReturnType(HttpStatusCode.OK, new IOResponse { StatusCode = "200", Address = tenantregistryresult.Address, Country = tenantregistryresult.Country, PhoneNumber = tenantregistryresult.PhoneNumber, Email = eMail, TenantName = tenantregistryresult.TenantName, CreatedDate = tenantregistryresult.CreatedDate, Message = IdentityValidationMessage.IDENTITY_DATAFOUND_EMAIL_MESSAGE });
            }
            return ActionSet.ActionReturnType(HttpStatusCode.InternalServerError, new IOResponse { StatusCode = "500", Email = "", Message = IdentityValidationMessage.IDENTITY_LOGINTENANT_ERROR_MESSAGE });
        }


        public async Task<ActionReturnType> ChangePassword(string eMail, string oldPassword, string newPassword)
        {
            if (!string.IsNullOrEmpty(eMail) && !string.IsNullOrEmpty(oldPassword) && !string.IsNullOrEmpty(newPassword))
            {
                // Super admins are not tenants, so they are handled before the tenant lookup.
                var superAdminEmail = eMail.Trim();
                var normalizedSuperAdminEmail = superAdminEmail.ToLower();
                var superAdmin = await _unitOfWork.DbContext.SuperAdmins
                    .FirstOrDefaultAsync(sa => sa.IsActive
                        && sa.Email != null
                        && sa.Email.ToLower() == normalizedSuperAdminEmail);

                if (superAdmin != null)
                {
                    if (superAdmin.IsPasswordChanged)
                    {
                        return ActionSet.ActionReturnType(HttpStatusCode.OK, new IOResponse { Email = eMail, Message = IdentityValidationMessage.IDENTITY_DATAFOUND_PASSWORDALREADYCHANGED_MESSAGE });
                    }

                    if (!PasswordHasher.Verify(superAdmin.Password, oldPassword.Trim()))
                    {
                        return ActionSet.ActionReturnType(HttpStatusCode.NotFound, new IOResponse { Email = eMail, Message = IdentityValidationMessage.IDENTITY_DATANOTFOUND_LOGIN_MESSAGE });
                    }

                    superAdmin.IsPasswordChanged = true;
                    superAdmin.Password = PasswordHasher.Hash(newPassword);
                    await _unitOfWork.SaveChangesAsync();
                    return ActionSet.ActionReturnType(HttpStatusCode.OK, new IOResponse { Email = eMail, Message = IdentityValidationMessage.IDENTITY_DATAFOUND_PASSWORDCHANGESUCCESS_MESSAGE });
                }

                var tenantregistryresult = await GetTenantByEmailAsync(eMail);
                if (tenantregistryresult == null)
                    return ActionSet.ActionReturnType(HttpStatusCode.NotFound, new IOResponse { Email = eMail, Message = IdentityValidationMessage.IDENTITY_DATANOTFOUND_LOGIN_MESSAGE });
                string tenantId = tenantregistryresult.TenantId;
                var tenantstoreinforesult = await GetTenantStoreByTenantIdAsync(tenantId);
                if (tenantstoreinforesult != null && tenantstoreinforesult.IsPasswordChanged == false
                    && PasswordHasher.Verify(tenantstoreinforesult.Password, oldPassword.Trim()))
                {
                    tenantstoreinforesult.IsPasswordChanged = true;
                    tenantstoreinforesult.Password = PasswordHasher.Hash(newPassword);
                        await _unitOfWork.SaveChangesAsync();
                    return ActionSet.ActionReturnType(HttpStatusCode.OK, new IOResponse { Email = eMail, Message = IdentityValidationMessage.IDENTITY_DATAFOUND_PASSWORDCHANGESUCCESS_MESSAGE });
                }
                else if (tenantstoreinforesult != null && tenantstoreinforesult.IsPasswordChanged == true)
                {
                    return ActionSet.ActionReturnType(HttpStatusCode.OK, new IOResponse { Email = eMail, Message = IdentityValidationMessage.IDENTITY_DATAFOUND_PASSWORDALREADYCHANGED_MESSAGE });
                }
            }
            else
            {
                return ActionSet.ActionReturnType(HttpStatusCode.OK, new IOResponse { Email = eMail, Message = IdentityValidationMessage.IDENTITY_TENANTPASSWORDDATA_NOT_FOUND });
            }
            return ActionSet.ActionReturnType(HttpStatusCode.InternalServerError, new IOResponse { Email = "", Message = IdentityValidationMessage.IDENTITY_LOGINTENANT_ERROR_MESSAGE });
        }

        private async Task<bool> CreateTenantUserInfoAsync(TenantDto tenantDto, string tenantid)
        {
            string password = string.Empty;
            password = Utility.encode(tenantDto.Email, 8);
            TenantStoreInfoEntity TenantStoreInfoEntity = new TenantStoreInfoEntity();
            TenantStoreInfoEntity.TenantId = tenantid;
            TenantStoreInfoEntity.Name = tenantDto.TenantName;
            TenantStoreInfoEntity.Status = "InActive";
            TenantStoreInfoEntity.IsPasswordChanged = false;
            TenantStoreInfoEntity.Password = PasswordHasher.Hash(password);
            List<string> storeCodes = new List<string>();
            for (int i = 0; i < tenantDto.NoOfStores; i++)
            {
                string code = Utility.GenerateStoreCode(3);
                storeCodes.Add(code);
            }
            TenantStoreInfoEntity.Storecodes = storeCodes;
            // Resolve tenant-level currency defaults from incoming DTO or fall back to country mapping
            var providedCurrencyCode = tenantDto.CurrencyCode?.Trim();
            var providedCurrencySymbol = tenantDto.CurrencySymbol?.Trim();
            var providedCurrencyLocale = tenantDto.CurrencyLocale?.Trim();
            int? providedCurrencyDigits = tenantDto.CurrencyDecimalDigits;

            var defaultCfg = CountryCurrencyMapping.GetDefaultForCountry(tenantDto.Country);
            TenantStoreInfoEntity.CurrencyCode = string.IsNullOrWhiteSpace(providedCurrencyCode) ? defaultCfg?.CurrencyCode : providedCurrencyCode;
            TenantStoreInfoEntity.CurrencySymbol = string.IsNullOrWhiteSpace(providedCurrencySymbol) ? defaultCfg?.CurrencySymbol : providedCurrencySymbol;
            TenantStoreInfoEntity.CurrencyLocale = string.IsNullOrWhiteSpace(providedCurrencyLocale) ? defaultCfg?.CurrencyLocale : providedCurrencyLocale;
            TenantStoreInfoEntity.CurrencyDecimalDigits = providedCurrencyDigits ?? defaultCfg?.DecimalDigits;

            _unitOfWork.DbContext.TenantStoreInfos.Add(TenantStoreInfoEntity);
            await _unitOfWork.SaveChangesAsync();
            // Send registration email in the background so slow mail servers do not delay
            // or time out the API response. Do not await the send here -- log failures
            // asynchronously. Returning true indicates the registration completed and
            // the email delivery is being attempted in the background.
            _ = Task.Run(async () =>
            {
                try
                {
                    var isRegistrationMailSent = await SendEmailToRegisterTenantAsync(tenantDto.Email, password, storeCodes, tenantDto.TenantName);
                    if (!isRegistrationMailSent)
                    {
                        _logger?.LogWarning("Tenant {TenantId} registered but registration email was not delivered to {Email}.", tenantid, tenantDto?.Email);
                    }
                }
                catch (Exception ex)
                {
                    // Failures while sending mail should not affect the registration flow.
                    _logger?.LogError(ex, "Background email send failed for tenant {TenantId} to {Email}.", tenantid, tenantDto?.Email);
                }
            });

            return true;
        }
        private async Task<bool> SendEmailToChangePasswordAsync(string eMail, string password)
        {
            var displayName = string.IsNullOrWhiteSpace(eMail)
                ? "Tenant"
                : eMail.Split('@')[0].Replace('.', ' ').Replace('_', ' ');

            StringBuilder sb = new StringBuilder();
            sb.Append("<div style='font-family:Segoe UI,Arial,sans-serif;color:#1f2937;font-size:14px;line-height:1.5;'>");
            sb.Append($"<p>Dear <strong>{System.Net.WebUtility.HtmlEncode(displayName)}</strong>,</p>");
            sb.Append("<p>Your password reset request has been processed successfully.</p>");
            sb.Append("<p><strong>Account Details</strong></p>");
            sb.Append("<table style='border-collapse:collapse;border:1px solid #d1d5db;min-width:480px;'>");
            sb.Append("<tr style='background:#f3f4f6;'><th style='text-align:left;padding:8px;border:1px solid #d1d5db;'>Field</th><th style='text-align:left;padding:8px;border:1px solid #d1d5db;'>Value</th></tr>");
            sb.Append($"<tr><td style='padding:8px;border:1px solid #d1d5db;'>Registered Email</td><td style='padding:8px;border:1px solid #d1d5db;'>{System.Net.WebUtility.HtmlEncode(eMail)}</td></tr>");
            sb.Append($"<tr><td style='padding:8px;border:1px solid #d1d5db;'>Temporary Password</td><td style='padding:8px;border:1px solid #d1d5db;'>{System.Net.WebUtility.HtmlEncode(password)}</td></tr>");
            sb.Append($"<tr><td style='padding:8px;border:1px solid #d1d5db;'>Generated On</td><td style='padding:8px;border:1px solid #d1d5db;'>{DateTime.UtcNow:dd/MM/yyyy HH:mm} UTC</td></tr>");
            sb.Append("</table>");
            sb.Append("<p>Please sign in and change your password immediately for security.</p>");
            sb.Append("<p>If this request was not initiated by you, contact LMS support right away.</p>");
            sb.Append("<p>Regards,<br/>LMS Support Team</p>");
            sb.Append("<p style='color:#6b7280;font-size:12px;'>This is an automated email. Please do not reply.</p>");
            sb.Append("</div>");

            var configuredAlertSubject = _mailConfiguration?.AlertMailSubject;
            string ReportSubject = string.IsNullOrWhiteSpace(configuredAlertSubject) || IsLegacySubject(configuredAlertSubject)
                ? "LMS Password Reset Notification"
                : configuredAlertSubject.Trim();
            string ReportBody = sb.ToString();
            string FileName = string.Empty;
            try
            {
                await SendMail(eMail, FileName, ReportSubject, ReportBody);
                return true;
            }
            catch (Exception ex)
            {
                // Mail delivery must not fail the password reset itself. Log for diagnostics.
                _logger?.LogError(ex, "Failed to send change-password email to {Email}", eMail);
                return false;
            }
        }
        private async Task<bool> SendEmailToRegisterTenantAsync(string eMail, string password, List<string> storeCodes, string tenantName)
        {
            storeCodes ??= new List<string>();
            var resolvedTenantName = string.IsNullOrWhiteSpace(tenantName) ? "Tenant" : tenantName.Trim();

            StringBuilder sb = new StringBuilder();
            sb.Append("<div style='font-family:Segoe UI,Arial,sans-serif;color:#1f2937;font-size:14px;line-height:1.5;'>");
            sb.Append($"<p>Dear <strong>{System.Net.WebUtility.HtmlEncode(resolvedTenantName)}</strong>,</p>");
            sb.Append("<p>Your tenant registration has been completed successfully.</p>");
            sb.Append("<p><strong>Registration Details</strong></p>");
            sb.Append("<table style='border-collapse:collapse;border:1px solid #d1d5db;min-width:520px;'>");
            sb.Append("<tr style='background:#f3f4f6;'><th style='text-align:left;padding:8px;border:1px solid #d1d5db;'>Field</th><th style='text-align:left;padding:8px;border:1px solid #d1d5db;'>Value</th></tr>");
            sb.Append($"<tr><td style='padding:8px;border:1px solid #d1d5db;'>Tenant Name</td><td style='padding:8px;border:1px solid #d1d5db;'>{System.Net.WebUtility.HtmlEncode(resolvedTenantName)}</td></tr>");
            sb.Append($"<tr><td style='padding:8px;border:1px solid #d1d5db;'>Registered Email</td><td style='padding:8px;border:1px solid #d1d5db;'>{System.Net.WebUtility.HtmlEncode(eMail)}</td></tr>");
            sb.Append($"<tr><td style='padding:8px;border:1px solid #d1d5db;'>Temporary Password</td><td style='padding:8px;border:1px solid #d1d5db;'>{System.Net.WebUtility.HtmlEncode(password)}</td></tr>");
            sb.Append($"<tr><td style='padding:8px;border:1px solid #d1d5db;'>Store Count</td><td style='padding:8px;border:1px solid #d1d5db;'>{storeCodes.Count}</td></tr>");
            sb.Append($"<tr><td style='padding:8px;border:1px solid #d1d5db;'>Store Codes</td><td style='padding:8px;border:1px solid #d1d5db;'>{System.Net.WebUtility.HtmlEncode(string.Join(", ", storeCodes))}</td></tr>");
            sb.Append("<tr><td style='padding:8px;border:1px solid #d1d5db;'>Status</td><td style='padding:8px;border:1px solid #d1d5db;'>Inactive</td></tr>");
            sb.Append($"<tr><td style='padding:8px;border:1px solid #d1d5db;'>Registration Date</td><td style='padding:8px;border:1px solid #d1d5db;'>{DateTime.UtcNow:dd/MM/yyyy HH:mm} UTC</td></tr>");
            sb.Append("</table>");
            sb.Append("<p>Please sign in and change your password on first login.</p>");
            sb.Append("<p>If you need assistance, please contact LMS support.</p>");
            sb.Append("<p>Regards,<br/>LMS Support Team</p>");
            sb.Append("<p style='color:#6b7280;font-size:12px;'>This is an automated email. Please do not reply.</p>");
            sb.Append("</div>");

            string ReportSubject = string.IsNullOrWhiteSpace(_mailSubject) || IsLegacySubject(_mailSubject)
                ? "Welcome to LMS - Tenant Registration Successful"
                : _mailSubject.Trim();
            string ReportBody = sb.ToString();
            string FileName = string.Empty;
            try
            {
                await SendMail(eMail, FileName, ReportSubject, ReportBody);
                return true;
            }
            catch (Exception ex)
            {
                // Mail delivery failure should not break tenant registration.
                _logger?.LogError(ex, "Failed to send tenant registration email to {Email}. StoreCodes: {StoreCodes}", eMail, string.Join(",", storeCodes ?? new List<string>()));
                return false;
            }
        }
        public async Task SendMail(string eMail, string successFile, string subject, string body)
        {
            var userName = _mailConfiguration.Username;
            var password = _mailConfiguration.Password;
            if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
            {
                throw new InvalidOperationException("MailConfiguration:Username/Password are not configured. Set them via user-secrets or the MailConfiguration__Username and MailConfiguration__Password environment variables.");
            }

            var host = string.IsNullOrWhiteSpace(_mailConfiguration.SmtpServer) ? "smtp.gmail.com" : _mailConfiguration.SmtpServer;
            var port = _mailConfiguration.Port > 0 ? _mailConfiguration.Port : 465;

            // Gmail only accepts a From address that matches the authenticated mailbox or one of its verified aliases.
            var fromAddress = string.IsNullOrWhiteSpace(_mailFrom) ? userName : _mailFrom;

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("Excel Laundry Services", fromAddress));
            message.To.Add(new MailboxAddress("ELS Tenant", eMail));
            message.Subject = subject;
            message.Body = new BodyBuilder { HtmlBody = body }.ToMessageBody();

            // Port 465 uses implicit TLS; 587 negotiates STARTTLS. Some networks block the
            // STARTTLS upgrade on 587, so implicit TLS is the safer default.
            var secureOption = port == 465
                ? SecureSocketOptions.SslOnConnect
                : SecureSocketOptions.StartTls;

            using var client = new MailKit.Net.Smtp.SmtpClient();
            await client.ConnectAsync(host, port, secureOption);
            await client.AuthenticateAsync(userName, password);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);
        }


        public async Task<ActionReturnType> CheckTenantEmail(string eMail)
        {
            if (!string.IsNullOrEmpty(eMail))
            {
                var tenantregistryresult = await GetTenantByEmailAsync(eMail);
                if (tenantregistryresult == null)
                    return ActionSet.ActionReturnType(HttpStatusCode.NotFound, new IOResponse { StatusCode = "404", Email = eMail, Message = IdentityValidationMessage.IDENTITY_DATANOTFOUND_EMAIL_MESSAGE });
                else
                    return ActionSet.ActionReturnType(HttpStatusCode.OK, new IOResponse { StatusCode = "200", Email = eMail, Message = IdentityValidationMessage.IDENTITY_DATAFOUND_EMAIL_MESSAGE });
            }
            return ActionSet.ActionReturnType(HttpStatusCode.InternalServerError, new IOResponse { StatusCode = "500", Email = "", Message = IdentityValidationMessage.IDENTITY_LOGINTENANT_ERROR_MESSAGE });
        }

        public async Task<ActionReturnType> GetTenantStoreDetails(string eMail)
        {
            if (!string.IsNullOrEmpty(eMail))
            {
                // Super admins own no stores. Without this they look identical to a store user
                // (empty store list), so the login screen must be told the real role.
                var superAdminEmail = eMail.Trim();
                var normalizedSuperAdminEmail = superAdminEmail.ToLower();
                var isSuperAdmin = await _unitOfWork.DbContext.SuperAdmins
                    .AnyAsync(sa => sa.IsActive
                        && sa.Email != null
                        && sa.Email.ToLower() == normalizedSuperAdminEmail);

                if (isSuperAdmin)
                {
                    return ActionSet.ActionReturnType(HttpStatusCode.OK, new IOResponse { StatusCode = "200", Email = eMail, Storecodes = new List<string>(), UserRole = "SuperAdmin" });
                }

                var tenantregistryresult = await GetTenantByEmailAsync(eMail);
                if (tenantregistryresult == null)
                    return ActionSet.ActionReturnType(HttpStatusCode.OK, new IOResponse { StatusCode = "200", Email = eMail, Message = IdentityValidationMessage.IDENTITY_DATANOTFOUND_EMAIL_MESSAGE, Storecodes = new List<string>() });
                string tenantId = tenantregistryresult.TenantId;
                var tenantstoreinforesult = await GetTenantStoreByTenantIdAsync(tenantId);
                if (tenantstoreinforesult == null)
                {
                    var generatedStores = new List<string>();
                    var storeCount = tenantregistryresult.NoOfStores > 0 ? tenantregistryresult.NoOfStores : 1;

                    for (int i = 0; i < storeCount; i++)
                    {
                        generatedStores.Add(Utility.GenerateStoreCode(3));
                    }

                    tenantstoreinforesult = new TenantStoreInfoEntity
                    {
                        TenantId = tenantId,
                        Name = tenantregistryresult.TenantName,
                        Status = "InActive",
                        IsPasswordChanged = false,
                        Password = string.Empty,
                        Storecodes = generatedStores
                    };

                    _unitOfWork.DbContext.TenantStoreInfos.Add(tenantstoreinforesult);
            await _unitOfWork.SaveChangesAsync();

            // Ensure the TenantStoreInfos.Status column reflects the current activation state
            try
            {
                var tsi = await GetTenantStoreByTenantIdAsync(tenantId);
                if (tsi != null)
                {
                    // Determine whether any store for this tenant is marked active in TenantStoreStatuses
                    var anyActive = await _unitOfWork.DbContext.TenantStoreStatuses
                        .AnyAsync(s => s.TenantId == tenantId && s.IsActive);

                    tsi.Status = anyActive ? "Active" : "InActive";
                    // Save status change to TenantStoreInfos
                    await _unitOfWork.SaveChangesAsync();
                }
            }
            catch
            {
                // ignore failures updating mirror status to avoid breaking activation flow
            }
                }

                List<string> stores = tenantstoreinforesult.Storecodes ?? new List<string>();

                // Build per-store configuration summaries so dashboard consumers can display currency without extra requests.
                var storeConfigs = new List<StoreConfigurationDto>();
                foreach (var code in stores)
                {
                    var normalized = (code ?? string.Empty).Trim();
                    var cfgEntity = await _unitOfWork.DbContext.StoreConfigurations
                        .FirstOrDefaultAsync(x => x.TenantId == tenantregistryresult.TenantId && x.StoreCode == normalized);

                    string cfgJson = null;
                    string currencyCode = null;
                    string currencySymbol = null;
                    string currencyLocale = null;
                    int? currencyDigits = null;

                    if (cfgEntity != null)
                    {
                        cfgJson = BuildConfigurationJsonWithCurrencyOverrides(cfgEntity);
                        currencyCode = cfgEntity.CurrencyCode;
                        currencySymbol = cfgEntity.CurrencySymbol;
                        currencyLocale = cfgEntity.CurrencyLocale;
                        currencyDigits = cfgEntity.CurrencyDecimalDigits;
                    }
                    else
                    {
                        // Fall back to tenant-level configuration or tenant store info currency
                        var tenantLevelCfg = await _unitOfWork.DbContext.StoreConfigurations
                            .FirstOrDefaultAsync(x => x.TenantId == tenantregistryresult.TenantId && (x.StoreCode == null || x.StoreCode == string.Empty));

                        cfgJson = tenantLevelCfg != null ? BuildConfigurationJsonWithCurrencyOverrides(tenantLevelCfg) : string.Empty;

                        // Resolve currency precedence: TenantStoreInfos -> tenant-level cfg -> country defaults
                        var tsi = tenantstoreinforesult;
                        if (!string.IsNullOrWhiteSpace(tsi?.CurrencyCode)) currencyCode = tsi.CurrencyCode;
                        if (!string.IsNullOrWhiteSpace(tsi?.CurrencySymbol)) currencySymbol = tsi.CurrencySymbol;
                        if (!string.IsNullOrWhiteSpace(tsi?.CurrencyLocale)) currencyLocale = tsi.CurrencyLocale;
                        if (tsi?.CurrencyDecimalDigits.HasValue == true) currencyDigits = tsi.CurrencyDecimalDigits;

                        if (string.IsNullOrWhiteSpace(currencyCode) || string.IsNullOrWhiteSpace(currencySymbol) || string.IsNullOrWhiteSpace(currencyLocale) || currencyDigits == null)
                        {
                            if (tenantLevelCfg != null)
                            {
                                currencyCode = currencyCode ?? tenantLevelCfg.CurrencyCode;
                                currencySymbol = currencySymbol ?? tenantLevelCfg.CurrencySymbol;
                                currencyLocale = currencyLocale ?? tenantLevelCfg.CurrencyLocale;
                                currencyDigits = currencyDigits ?? tenantLevelCfg.CurrencyDecimalDigits;
                            }
                        }

                        if (string.IsNullOrWhiteSpace(currencyCode) || string.IsNullOrWhiteSpace(currencySymbol) || string.IsNullOrWhiteSpace(currencyLocale) || currencyDigits == null)
                        {
                            var defaultCfg = CountryCurrencyMapping.GetDefaultForCountry(tenantregistryresult.Country);
                            if (defaultCfg != null)
                            {
                                currencyCode = currencyCode ?? defaultCfg.CurrencyCode;
                                currencySymbol = currencySymbol ?? defaultCfg.CurrencySymbol;
                                currencyLocale = currencyLocale ?? defaultCfg.CurrencyLocale;
                                currencyDigits = currencyDigits ?? defaultCfg.DecimalDigits;
                            }
                        }
                    }

                    storeConfigs.Add(new StoreConfigurationDto
                    {
                        StoreCode = normalized,
                        ConfigurationJson = cfgJson,
                        CurrencyCode = currencyCode,
                        CurrencySymbol = currencySymbol,
                        CurrencyLocale = currencyLocale,
                        CurrencyDecimalDigits = currencyDigits
                    });
                }

                // Also include per-store activation statuses so callers that request
                // full tenant store details receive authoritative IsActive flags
                // without needing a separate API call.
                var tenantStatuses = await _unitOfWork.DbContext.TenantStoreStatuses
                    .Where(x => x.TenantId == tenantregistryresult.TenantId)
                    .ToListAsync();

                var statusDtos = stores
                    .Select(code =>
                    {
                        var status = tenantStatuses.FirstOrDefault(s => string.Equals(s.StoreCode, code, StringComparison.OrdinalIgnoreCase));
                        // If no per-store status record exists, fall back to the tenant-level
                        // TenantStoreInfo.Status mirror which may be updated to "Active" when
                        // any store for the tenant is marked active.
                        var fallbackActive = string.Equals(tenantstoreinforesult?.Status, "Active", StringComparison.OrdinalIgnoreCase);
                        return new TenantStoreStatusDto
                        {
                            StoreCode = code,
                            IsActive = status?.IsActive ?? fallbackActive,
                            ActivatedBy = status?.ActivatedBy,
                            ActivatedDate = status?.ActivatedDate,
                            TenantId = tenantregistryresult?.TenantId,
                            TenantName = tenantregistryresult?.TenantName,
                            TenantEmail = tenantregistryresult?.Email
                        };
                    })
                    .ToList();

                return ActionSet.ActionReturnType(HttpStatusCode.OK, new IOResponse
                {
                    StatusCode = "200",
                    Email = eMail,
                    Storecodes = stores,
                    StoreConfigurations = storeConfigs,
                    StoreStatuses = statusDtos
                });
            }
            else
            {
                return ActionSet.ActionReturnType(HttpStatusCode.OK, new IOResponse { StatusCode = "200", Email = eMail, Message = IdentityValidationMessage.IDENTITY_DATANOTFOUND_EMAIL_MESSAGE, Storecodes = new List<string>() });
            }
        }

        public async Task<ActionReturnType> CheckIsPasswordChangedByTenant(string eMail)
        {
            if (!string.IsNullOrEmpty(eMail))
            {
                var tenantregistryresult = await GetTenantByEmailAsync(eMail);
                if (tenantregistryresult == null)
                    return ActionSet.ActionReturnType(HttpStatusCode.NotFound, new IOResponse { StatusCode = "404", Email = eMail, Message = IdentityValidationMessage.IDENTITY_DATANOTFOUND_EMAIL_MESSAGE });
                string tenantId = tenantregistryresult.TenantId;
                var passchangeresult = await GetTenantStoreByTenantIdAsync(tenantId);
                if (passchangeresult != null)
                {
                    string status = Convert.ToString(passchangeresult.IsPasswordChanged);
                    return ActionSet.ActionReturnType(HttpStatusCode.OK, new IOResponse { StatusCode = "200", Email = eMail, Message = status });
                }
            }
            else
            {
                return ActionSet.ActionReturnType(HttpStatusCode.OK, new IOResponse { StatusCode = "200", Email = eMail, Message = IdentityValidationMessage.IDENTITY_DATANOTFOUND_EMAIL_MESSAGE });
            }
            return ActionSet.ActionReturnType(HttpStatusCode.InternalServerError, new IOResponse { StatusCode = "500", Email = "", Message = IdentityValidationMessage.IDENTITY_LOGINTENANT_ERROR_MESSAGE });
        }

        public async Task<ActionReturnType> ChangePasswordForTenant(string Email, string NewPassword, string OldPassword)
        {
            if (!string.IsNullOrEmpty(Email))
            {
                // Super admins are not tenants, so they are handled before the tenant lookup.
                var superAdminEmail = Email.Trim();
                var normalizedSuperAdminEmail = superAdminEmail.ToLower();
                var superAdmin = await _unitOfWork.DbContext.SuperAdmins
                    .FirstOrDefaultAsync(sa => sa.IsActive
                        && sa.Email != null
                        && sa.Email.ToLower() == normalizedSuperAdminEmail);

                if (superAdmin != null)
                {
                    superAdmin.IsPasswordChanged = true;
                    superAdmin.Password = PasswordHasher.Hash(NewPassword);
            await _unitOfWork.SaveChangesAsync();
                    return ActionSet.ActionReturnType(HttpStatusCode.OK, new IOResponse { StatusCode = "200", Email = Email, Message = IdentityValidationMessage.IDENTITY_DATAFOUNDCHANGEPASSWORDL_MESSAGE });
                }

                var tenantregistryresult = await GetTenantByEmailAsync(Email);
                if (tenantregistryresult == null)
                    return ActionSet.ActionReturnType(HttpStatusCode.NotFound, new IOResponse { StatusCode = "404", Email = Email, Message = IdentityValidationMessage.IDENTITY_DATANOTFOUND_EMAIL_MESSAGE });
                string tenantId = tenantregistryresult.TenantId;
                var changepassresult = await GetTenantStoreByTenantIdAsync(tenantId);
                if (changepassresult != null)
                {
                    changepassresult.IsPasswordChanged = true;
                    changepassresult.Password = PasswordHasher.Hash(NewPassword);
                await _unitOfWork.SaveChangesAsync();
                    return ActionSet.ActionReturnType(HttpStatusCode.OK, new IOResponse { StatusCode = "200", Email = Email, Message = IdentityValidationMessage.IDENTITY_DATAFOUNDCHANGEPASSWORDL_MESSAGE });
                }
            }
            else
            {
                return ActionSet.ActionReturnType(HttpStatusCode.OK, new IOResponse { StatusCode = "200", Email = Email, Message = IdentityValidationMessage.IDENTITY_DATANOTFOUND_EMAIL_MESSAGE });
            }
            return ActionSet.ActionReturnType(HttpStatusCode.InternalServerError, new IOResponse { StatusCode = "500", Email = Email, Message = IdentityValidationMessage.IDENTITY_LOGINTENANT_ERROR_MESSAGE });
        }

        public async Task<ActionReturnType> ForgotPasswordForTenant(string Email)
        {
            if (!string.IsNullOrEmpty(Email))
            {
                // Super admins are not tenants, so they are handled before the tenant lookup.
                var superAdminEmail = Email.Trim();
                var normalizedSuperAdminEmail = superAdminEmail.ToLower();
                var superAdmin = await _unitOfWork.DbContext.SuperAdmins
                    .FirstOrDefaultAsync(sa => sa.IsActive
                        && sa.Email != null
                        && sa.Email.ToLower() == normalizedSuperAdminEmail);

                if (superAdmin != null)
                {
                    var superAdminPassword = Utility.encode(Email, 8);
                    superAdmin.IsPasswordChanged = false;
                    superAdmin.Password = PasswordHasher.Hash(superAdminPassword);
                await _unitOfWork.SaveChangesAsync();
                    await SendEmailToChangePasswordAsync(Email, superAdminPassword);
                    return ActionSet.ActionReturnType(HttpStatusCode.OK, new IOResponse { Email = Email, Message = IdentityValidationMessage.IDENTITY_DATAFOUND_CHANGEPASSWORDSUCCESS_MESSAGE });
                }

                var tenantfogotpasswordresult = await GetTenantByEmailAsync(Email);
                if (tenantfogotpasswordresult == null)
                    return ActionSet.ActionReturnType(HttpStatusCode.NotFound, new IOResponse { StatusCode = "404", Email = Email, Message = IdentityValidationMessage.IDENTITY_DATANOTFOUND_EMAIL_MESSAGE });

                string tenantId = tenantfogotpasswordresult.TenantId;
                var forgotpassresult = await GetTenantStoreByTenantIdAsync(tenantId);
                if (forgotpassresult != null)
                {
                    string password = string.Empty;
                    password = Utility.encode(Email, 8);
                    forgotpassresult.IsPasswordChanged = false;
                    forgotpassresult.Password = PasswordHasher.Hash(password);
            await _unitOfWork.SaveChangesAsync();
                    await SendEmailToChangePasswordAsync(Email, password);
                    return ActionSet.ActionReturnType(HttpStatusCode.OK, new IOResponse { Email = Email, Message = IdentityValidationMessage.IDENTITY_DATAFOUND_CHANGEPASSWORDSUCCESS_MESSAGE });
                }
            }
            else
            {
                return ActionSet.ActionReturnType(HttpStatusCode.OK, new IOResponse { StatusCode = "200", Email = Email, Message = IdentityValidationMessage.IDENTITY_DATANOTFOUND_EMAIL_MESSAGE });
            }
            return ActionSet.ActionReturnType(HttpStatusCode.InternalServerError, new IOResponse { StatusCode = "500", Email = Email, Message = IdentityValidationMessage.IDENTITY_LOGINTENANT_ERROR_MESSAGE });
        }

        private async Task<TenantEntity> GetTenantByEmailAsync(string email)
        {
            var normalizedEmail = (email ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedEmail))
            {
                return null;
            }

            var tenant = await _unitOfWork.DbContext.Tenants
                .Where(em => em.Email == normalizedEmail)
                .Select(em => new TenantEntity
                {
                    Id = em.Id,
                    NoOfStores = em.NoOfStores,
                    TenantId = em.TenantId,
                    TenantName = em.TenantName,
                    Email = em.Email,
                    CreatedDate = em.CreatedDate,
                    ModifiedDate = em.ModifiedDate,
                    ApprovalStatus = em.ApprovalStatus,
                    ApprovalReason = em.ApprovalReason,
                    ApprovedBy = em.ApprovedBy,
                    ApprovalDate = em.ApprovalDate
                })
                .FirstOrDefaultAsync();
            if (tenant != null)
            {
                return tenant;
            }

            var normalizedEmailLower = normalizedEmail.ToLower();
            return await _unitOfWork.DbContext.Tenants
                .Where(em => em.Email != null && em.Email.Trim().ToLower() == normalizedEmailLower)
                .Select(em => new TenantEntity
                {
                    Id = em.Id,
                    NoOfStores = em.NoOfStores,
                    TenantId = em.TenantId,
                    TenantName = em.TenantName,
                    Email = em.Email,
                    CreatedDate = em.CreatedDate,
                    ModifiedDate = em.ModifiedDate,
                    ApprovalStatus = em.ApprovalStatus,
                    ApprovalReason = em.ApprovalReason,
                    ApprovedBy = em.ApprovedBy,
                    ApprovalDate = em.ApprovalDate
                })
                .FirstOrDefaultAsync();
        }

        private Task<TenantStoreInfoEntity> GetTenantStoreByTenantIdAsync(string tenantId)
            => _unitOfWork.DbContext.TenantStoreInfos.FirstOrDefaultAsync(tsi => tsi.TenantId == tenantId);

    }
}
