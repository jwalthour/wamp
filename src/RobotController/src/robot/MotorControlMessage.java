package robot;

public class MotorControlMessage 
{
    public Boolean[] Fwd;
    public short[] Power;
    public int EffectiveTimeMs;

	public String GetMotorControlMessage(int channel)
	{
		char fwd_rev = Fwd[channel]? 'F' : 'R';
		return String.format("S %01d,%c,%03d", channel, fwd_rev, Power[channel]);
	}
	
	public String GetWatchdogMessage()
	{
		int time_ms = Math.min(999, EffectiveTimeMs);
		time_ms = Math.max(0, time_ms);
		return String.format("W %03d", time_ms);
	}
}
