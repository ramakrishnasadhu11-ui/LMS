using AutoMapper;
using LMS.Identity.DTO;
using LMS.Identity.DTO.Entities;

namespace LMS.Identity.BusinessSerive.Mapper
{
    public class MapperProfile : Profile
    {
        public MapperProfile()
        {
            AllowNullDestinationValues = true;
            CreateMap<TenantDto, TenantEntity>().IgnoreAllPropertiesWithAnInaccessibleSetter().ReverseMap();
        }
    }
}
