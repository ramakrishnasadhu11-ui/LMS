using LMS.Identity.BusinessSerive.Interfaces;
using LMS.Identity.BusinessSerive.Data;
using LMS.Identity.DTO.Entities;
using Microsoft.EntityFrameworkCore;
using LMS.Core.Repository.UnitOfWork;
using AutoMapper;
using LMS.Identity.DTO;
using System;
using System.Threading.Tasks;
using LMS.Identity.Utilities;
using System.Net;
using LMS.Identity.BusinessSerive.Common;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MailKit.Security;
using MimeKit;
using Microsoft.Extensions.Logging;

namespace LMS.Identity.BusinessSerive.Services
{
    public class LoginService : ILoginService
    {
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
            if (tenantDto == null)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.NoContent, new IOResponse { StatusCode = "204", TenantId = "", Message = IdentityValidationMessage.IDENTITY_DATANOTFOUND_ERROR_MESSAGE });
            }
            else if (tenantDto != null)
            {
                var result = await _unitOfWork.DbContext.Tenants.FirstOrDefaultAsync(em => em.Email == tenantDto.Email);
                if (result != null)
                {
                    return ActionSet.ActionReturnType(HttpStatusCode.AlreadyReported, new IOResponse { StatusCode = "208", Message = IdentityValidationMessage.IDENTITY_DATAFOUND_ERROR_MESSAGE });
                }

                var tenantData = _mapper.Map<TenantEntity>(tenantDto);
                var guid = Guid.NewGuid();
                var tenantid = Convert.ToString(guid);
                tenantData.TenantId = tenantid;
                tenantData.ModifiedDate = DateTime.UtcNow;
                tenantData.CreatedDate = DateTime.UtcNow;
                // New tenants stay pending until a super admin approves them.
                tenantData.ApprovalStatus = TenantApprovalStatus.Pending;
                _unitOfWork.DbContext.Tenants.Add(tenantData);
                await _unitOfWork.SaveChangesAsync();
                // No-op reaffirmation to ensure earlier changes are stable.
                await CreateTenantUserInfoAsync(tenantDto, tenantid);
                return ActionSet.ActionReturnType(HttpStatusCode.Created, new IOResponse { StatusCode = "200", TenantId = tenantid, Message = IdentityValidationMessage.IDENTITY_INSERT_SUCCESS_MESSAGE, TenantName = tenantDto.TenantName });
            }
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

            await _unitOfWork.SaveChangesAsync();

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
                ConfigurationJson = config.ConfigurationJson,
                Message = string.Empty
            });
        }

        public async Task<ActionReturnType> NewStoreConfiguration(NewStoreConfigurationDto model)
        {
            if (model == null || string.IsNullOrWhiteSpace(model.TenantEmail) || string.IsNullOrWhiteSpace(model.StoreCode) || string.IsNullOrWhiteSpace(model.ConfigurationJson))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new IOResponse
                {
                    StatusCode = "400",
                    Message = "Tenant email, store code and configuration are required."
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
                    CreatedDate = utcNow,
                    CreatedBy = updatedBy,
                    ModifiedDate = utcNow,
                    ModifiedBy = updatedBy
                };

                _unitOfWork.DbContext.StoreConfigurations.Add(existing);
            }
            else
            {
                existing.ConfigurationJson = model.ConfigurationJson;
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
                    return ActionSet.ActionReturnType(HttpStatusCode.OK, new IOResponse { StatusCode = "200", Address = tenantregistryresult.Address, Country = tenantregistryresult.Country, PhoneNumber = tenantregistryresult.PhoneNumber, Email = eMail, TenantName = tenantregistryresult.TenantName, Message = IdentityValidationMessage.IDENTITY_DATAFOUND_EMAIL_MESSAGE });
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
            TenantStoreInfoEntity.Status = "ÏnActive";
            TenantStoreInfoEntity.IsPasswordChanged = false;
            TenantStoreInfoEntity.Password = PasswordHasher.Hash(password);
            List<string> storeCodes = new List<string>();
            for (int i = 0; i < tenantDto.NoOfStores; i++)
            {
                string code = Utility.GenerateStoreCode(3);
                storeCodes.Add(code);
            }
            TenantStoreInfoEntity.Storecodes = storeCodes;
            _unitOfWork.DbContext.TenantStoreInfos.Add(TenantStoreInfoEntity);
            await _unitOfWork.SaveChangesAsync();
            await SendEmailToRegisterTenantAsync(tenantDto.Email, password, storeCodes, tenantDto.TenantName);
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
                // For tenant registration we want visibility into mail failures.
                _logger?.LogError(ex, "Failed to send tenant registration email to {Email}. StoreCodes: {StoreCodes}", eMail, string.Join(",", storeCodes ?? new List<string>()));
                // Rethrow so the caller / pipeline can observe the failure during diagnostics.
                throw;
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
                        Status = "ÏnActive",
                        IsPasswordChanged = false,
                        Password = string.Empty,
                        Storecodes = generatedStores
                    };

                    _unitOfWork.DbContext.TenantStoreInfos.Add(tenantstoreinforesult);
            await _unitOfWork.SaveChangesAsync();
                }

                List<string> stores = tenantstoreinforesult.Storecodes ?? new List<string>();
                return ActionSet.ActionReturnType(HttpStatusCode.OK, new IOResponse { StatusCode = "200", Email = eMail, Storecodes = stores });
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
