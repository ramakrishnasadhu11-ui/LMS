using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using LMS.Master.BusinessSerive.Data;

namespace LMS.Master.BusinessSerive
{
    public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<MasterDbContext>
    {
        public MasterDbContext CreateDbContext(string[] args)
        {
            var options = new DbContextOptionsBuilder<MasterDbContext>();
            options.UseSqlServer("Server=RKNAIDUSADHU\\MSSQLSERVER01;Database=LMSDB;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true");
            return new MasterDbContext(options.Options);
        }
    }
}
