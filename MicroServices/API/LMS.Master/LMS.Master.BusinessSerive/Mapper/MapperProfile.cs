using AutoMapper;
using LMS.Master.DTO;
using LMS.Master.DTO.Entity;

namespace LMS.Master.BusinessSerive.Mapper
{
    public class MapperProfile : Profile
    {
        public MapperProfile()
        {
            AllowNullDestinationValues = true;
            CreateMap<CustomerEntity, CustomerDto>().ReverseMap();
            CreateMap<CustomerPreferenceEntity, CustomerPreferenceDto>().ReverseMap();
            CreateMap<PricingRulesEntity, PricingRulesDto>().ReverseMap();
            CreateMap<PaymentSettingsEntity, PaymentSettingsDto>().ReverseMap();
            CreateMap<TaxInvoiceSettingsEntity, TaxInvoiceSettingsDto>().ReverseMap();
            CreateMap<BarcodeTagSettingsEntity, BarcodeTagSettingsDto>().ReverseMap();
            CreateMap<WorkflowStatusEntity, WorkflowStatusDto>().ReverseMap();
            CreateMap<CustomerAdvanceEntity, CustomerAdvanceDto>().ReverseMap();
            CreateMap<LaundryOrderEntity, LaundryOrderDto>().ReverseMap();
            CreateMap<LaundryItemPriceEntity, LaundryItemPriceDto>().ReverseMap();
            CreateMap<StoreServiceMasterEntity, StoreServiceMasterDto>().ReverseMap();
            CreateMap<StoreItemMasterEntity, StoreItemMasterDto>().ReverseMap();
        }
    }
}
