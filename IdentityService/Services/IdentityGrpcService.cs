using Grpc.Core;
using IdentityService.Data;
using IdentityService.Messaging;
using IdentityService.Models;
using IdentityService.Protos;
using Microsoft.EntityFrameworkCore;

namespace IdentityService.Services
{
    public class IdentityGrpcService : Protos.IdentityGrpcService.IdentityGrpcServiceBase
    {
        private readonly AppDbContext _db;
        private readonly ServiceBusPublisher _publisher;
        private readonly ILogger<IdentityGrpcService> _logger;

        // Default role assigned to every new user on sign up
        private static readonly Guid StudentRoleId =
            Guid.Parse("00000000-0000-0000-0000-000000000001");

        public IdentityGrpcService(
            AppDbContext db,
            ServiceBusPublisher publisher,
            ILogger<IdentityGrpcService> logger)
        {
            _db = db;
            _publisher = publisher;
            _logger = logger;
        }

        // ── SignUp ─────────────────────────────────────────────────────
        public override async Task<SignUpReply> SignUp(
            SignUpRequest request, ServerCallContext context)
        {
            try
            {
                var emailLower = request.Email.ToLower();

                if (await _db.Users.AnyAsync(u => u.Email == emailLower))
                    return new SignUpReply
                    {
                        Succeeded = false,
                        Authenticated = false,
                        Email = request.Email
                    };

                var user = new User
                {
                    Email = emailLower,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password)
                };

                _db.Users.Add(user);

                // Assign Student role by default
                _db.UserRoles.Add(new UserRole
                {
                    UserId = user.Id,
                    RoleId = StudentRoleId
                });

                await _db.SaveChangesAsync();

                // Temporarily disabled until Verification-Service is ready
                // await _publisher.PublishUserRegisteredAsync(user.Id, user.Email);

                return new SignUpReply
                {
                    Succeeded = true,
                    Authenticated = false, // email not yet confirmed
                    Email = user.Email,
                    UserId = user.Id.ToString(),
                    FirstName = user.FirstName,
                    LastName = user.LastName
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during SignUp for {Email}", request.Email);
                return new SignUpReply { Succeeded = false, Authenticated = false };
            }
        }

        // ── Authenticate ───────────────────────────────────────────────
        public override async Task<AuthenticateReply> Authenticate(
            AuthenticateRequest request, ServerCallContext context)
        {
            try
            {
                var emailLower = request.Email.ToLower();
                var user = await _db.Users
                    .FirstOrDefaultAsync(u => u.Email == emailLower);

                if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
                    return new AuthenticateReply
                    {
                        Succeeded = false,
                        Authenticated = false
                    };

                return new AuthenticateReply
                {
                    Succeeded = true,
                    Authenticated = user.IsEmailVerified,
                    Email = user.Email,
                    UserId = user.Id.ToString(),
                    FirstName = user.FirstName,
                    LastName = user.LastName
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during Authenticate for {Email}", request.Email);
                return new AuthenticateReply { Succeeded = false, Authenticated = false };
            }
        }

        // ── CheckEmailConfirmStatus ────────────────────────────────────
        public override async Task<CheckEmailConfirmStatusReply> CheckEmailConfirmStatus(
            CheckEmailConfirmStatusRequest request, ServerCallContext context)
        {
            try
            {
                var user = await _db.Users
                    .FirstOrDefaultAsync(u => u.Email == request.Email.ToLower());

                if (user == null)
                    return new CheckEmailConfirmStatusReply
                    {
                        Succeeded = true,
                        Exists = false,
                        EmailConfirmed = false
                    };

                return new CheckEmailConfirmStatusReply
                {
                    Succeeded = true,
                    Exists = true,
                    EmailConfirmed = user.IsEmailVerified,
                    Email = user.Email,
                    UserId = user.Id.ToString()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during CheckEmailConfirmStatus for {Email}", request.Email);
                return new CheckEmailConfirmStatusReply { Succeeded = false };
            }
        }

        // ── ChangeEmailConfirmStatus ───────────────────────────────────
        // Called by Verification-Service (via Auth-Api) after user clicks
        // the confirmation link in their email
        public override async Task<ChangeEmailConfirmStatusReply> ChangeEmailConfirmStatus(
            ChangeEmailConfirmStatusRequest request, ServerCallContext context)
        {
            try
            {
                var user = await _db.Users
                    .FirstOrDefaultAsync(u => u.Email == request.Email.ToLower());

                if (user == null)
                    return new ChangeEmailConfirmStatusReply { Succeeded = false };

                user.IsEmailVerified = request.EmailConfirmed;
                user.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();

                return new ChangeEmailConfirmStatusReply
                {
                    Succeeded = true,
                    EmailConfirmed = user.IsEmailVerified,
                    Email = user.Email,
                    UserId = user.Id.ToString()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during ChangeEmailConfirmStatus for {Email}", request.Email);
                return new ChangeEmailConfirmStatusReply { Succeeded = false };
            }
        }

        // ── GetRoles ───────────────────────────────────────────────────
        public override async Task<GetRolesReply> GetRoles(
            GetRolesRequest request, ServerCallContext context)
        {
            try
            {
                if (!Guid.TryParse(request.UserId, out var userId))
                    return new GetRolesReply { Succeeded = false };

                var roles = await _db.UserRoles
                    .Where(ur => ur.UserId == userId)
                    .Include(ur => ur.Role)
                    .Select(ur => ur.Role.Name)
                    .ToListAsync();

                var reply = new GetRolesReply { Succeeded = true };
                reply.Roles.AddRange(roles);
                return reply;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during GetRoles for {UserId}", request.UserId);
                return new GetRolesReply { Succeeded = false };
            }
        }

        // ── GetUser ────────────────────────────────────────────────────
        public override async Task<GetUserReply> GetUser(
            GetUserRequest request, ServerCallContext context)
        {
            try
            {
                if (!Guid.TryParse(request.UserId, out var userId))
                    return new GetUserReply { Succeeded = false, Error = "Invalid user ID." };

                var user = await _db.Users
                    .Include(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
                    .FirstOrDefaultAsync(u => u.Id == userId);

                if (user == null)
                    return new GetUserReply { Succeeded = false, Error = "User not found." };

                return MapToGetUserReply(user);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during GetUser for {UserId}", request.UserId);
                return new GetUserReply { Succeeded = false, Error = "An unexpected error occurred." };
            }
        }

        // ── UpdateUser ─────────────────────────────────────────────────
        public override async Task<UpdateUserReply> UpdateUser(
            UpdateUserRequest request, ServerCallContext context)
        {
            try
            {
                if (!Guid.TryParse(request.UserId, out var userId))
                    return new UpdateUserReply { Succeeded = false, Error = "Invalid user ID." };

                var user = await _db.Users.FindAsync(userId);
                if (user == null)
                    return new UpdateUserReply { Succeeded = false, Error = "User not found." };

                if (!string.IsNullOrWhiteSpace(request.FirstName))
                    user.FirstName = request.FirstName;

                if (!string.IsNullOrWhiteSpace(request.LastName))
                    user.LastName = request.LastName;

                if (!string.IsNullOrWhiteSpace(request.PhoneNumber))
                    user.PhoneNumber = request.PhoneNumber;

                if (!string.IsNullOrWhiteSpace(request.Description))
                    user.Description = request.Description;

                if (!string.IsNullOrWhiteSpace(request.ProfilePictureUrl))
                    user.ProfilePictureUrl = request.ProfilePictureUrl;

                if (!string.IsNullOrWhiteSpace(request.Email))
                {
                    var emailLower = request.Email.ToLower();
                    var emailTaken = await _db.Users
                        .AnyAsync(u => u.Email == emailLower && u.Id != userId);

                    if (emailTaken)
                        return new UpdateUserReply
                        {
                            Succeeded = false,
                            Error = "Email is already in use."
                        };

                    user.Email = emailLower;
                }

                user.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();

                return new UpdateUserReply
                {
                    Succeeded = true,
                    UserId = user.Id.ToString(),
                    Email = user.Email,
                    FirstName = user.FirstName,
                    LastName = user.LastName,
                    PhoneNumber = user.PhoneNumber ?? string.Empty,
                    Description = user.Description ?? string.Empty,
                    ProfilePictureUrl = user.ProfilePictureUrl ?? string.Empty
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during UpdateUser for {UserId}", request.UserId);
                return new UpdateUserReply { Succeeded = false, Error = "An unexpected error occurred." };
            }
        }

        // ── DeleteUser ─────────────────────────────────────────────────
        public override async Task<DeleteUserReply> DeleteUser(
            DeleteUserRequest request, ServerCallContext context)
        {
            try
            {
                if (!Guid.TryParse(request.UserId, out var userId))
                    return new DeleteUserReply { Succeeded = false, Error = "Invalid user ID." };

                var user = await _db.Users.FindAsync(userId);
                if (user == null)
                    return new DeleteUserReply { Succeeded = false, Error = "User not found." };

                _db.Users.Remove(user);
                await _db.SaveChangesAsync();

                return new DeleteUserReply { Succeeded = true };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during DeleteUser for {UserId}", request.UserId);
                return new DeleteUserReply { Succeeded = false, Error = "An unexpected error occurred." };
            }
        }

        // ── Helper ─────────────────────────────────────────────────────
        private static GetUserReply MapToGetUserReply(User user)
        {
            var reply = new GetUserReply
            {
                Succeeded = true,
                UserId = user.Id.ToString(),
                Email = user.Email,
                FirstName = user.FirstName,
                LastName = user.LastName,
                PhoneNumber = user.PhoneNumber ?? string.Empty,
                Description = user.Description ?? string.Empty,
                ProfilePictureUrl = user.ProfilePictureUrl ?? string.Empty,
                CreatedAt = user.CreatedAt.ToString("O"),
                EmailConfirmed = user.IsEmailVerified
            };

            reply.Roles.AddRange(user.UserRoles.Select(ur => ur.Role.Name));
            return reply;
        }
    }
}
