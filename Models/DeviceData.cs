// Models/DeviceData.cs
namespace GPIMSWebServer.Models
{
    public class DeviceData
    {
        public List<ChannelData> Channels { get; set; } = new();
        public List<AuxData> AuxData { get; set; } = new();
        public List<CANData> CANData { get; set; } = new();
        public List<LINData> LINData { get; set; } = new();
        public List<AlarmData> AlarmData { get; set; } = new();
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string DeviceId { get; set; } = string.Empty;
    }

    public class ChannelData
    {
        public int ChannelNumber { get; set; }
        public ChannelStatus Status { get; set; }
        public ChannelMode Mode { get; set; }
        public int CycleNo { get; set; }
        public int StepNo { get; set; }
        public int CyclerLoop { get; set; }
        public double Voltage { get; set; }
        public double Current { get; set; }
        public double Capacity { get; set; }
        public double Power { get; set; }
        public double ChamberTemperature { get; set; }
        public TimeSpan StepTime { get; set; }
        public TimeSpan TotalTime { get; set; }
        public string TestName { get; set; } = string.Empty;
        public string Schedule { get; set; } = string.Empty;
    }

    public enum ChannelStatus
    {
        Idle,
        Rest,
        Discharge,
        Charge,
        Pause,
        Finish
    }

    public enum ChannelMode
    {
        Rest,
        ChargeCC,
        ChargeCCCV,
        ChargeCP,
        ChargeCPCV,
        DischargeCC,
        DischargeCCCV,
        DischargeCP,
        DischargeCPCV
    }

    public class AuxData
    {
        public int SensorId { get; set; }
        public string SensorName { get; set; } = string.Empty;
        public double Value { get; set; }
        public double SafeUpperLimit { get; set; }
        public double SafeLowerLimit { get; set; }
    }

    public class CANData
    {
        public string Name { get; set; } = string.Empty;
        public double Value { get; set; }
        public int BmsId { get; set; }
        public double Max { get; set; }
        public double Min { get; set; }
    }

    public class LINData
    {
        public string Name { get; set; } = string.Empty;
        public double Value { get; set; }
        public int BmsId { get; set; }
        public double Max { get; set; }
        public double Min { get; set; }
    }

    public class AlarmData
    {
        public string Name { get; set; } = string.Empty;
        public int Id { get; set; }
        public string Description { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public AlarmSeverity Severity { get; set; }
    }

    public enum AlarmSeverity
    {
        Info,
        Warning,
        Error,
        Critical
    }
}