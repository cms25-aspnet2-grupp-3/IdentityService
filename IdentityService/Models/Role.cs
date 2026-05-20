namespace IdentityService.Models
{
    public class Role
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty; // e.g. "Student", "Admin"

        public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
    }
}
