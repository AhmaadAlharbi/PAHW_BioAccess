using System;
using System.Collections.Generic;
using System.Text;

namespace BioAccess.Web.DTOs
{
    public class DeviceDto
    {
        public string DeviceId { get; set; } = "";
        public string DeviceName { get; set; } = "";
        public string Location { get; set; } = "";
        public string IPAddress { get; set; } = "";
        public bool IsOnline { get; set; }
        
    }
}
