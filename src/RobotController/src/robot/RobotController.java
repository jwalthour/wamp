package robot;

import java.io.BufferedReader;
import java.io.FileReader;
import java.io.IOException;

import com.google.gson.Gson;
import com.rabbitmq.client.Channel;
import com.rabbitmq.client.Connection;
import com.rabbitmq.client.ConnectionFactory;
import com.rabbitmq.client.QueueingConsumer;

import gnu.io.*;
import java.io.*;
import java.nio.charset.Charset;

/**
 * Note - strong design hints from the great people at RXTX:
 * http://rxtx.qbang.org/wiki/index.php/Two_way_communcation_with_the_serial_port
 * 
 * @author John Walthour
 *
 */
public class RobotController 
{
	private Settings mSettings = null;
	private static final String SETTINGS_FILENAME = "RobotControllerSettings.txt";
	
	/**
	 * @param args
	 */
	public static void main(String[] args) 
	{
		RobotController rc = new RobotController();
		rc.Run(SETTINGS_FILENAME);
	}
	
	public void Run(String settings_filename)
	{
		// Load settings
		Gson gson = new Gson();
		try 
		{
			BufferedReader infile = new BufferedReader(new FileReader(settings_filename));
			String settings_string = "";
			while(infile.ready())
				settings_string += infile.readLine() + "\n";
			
			mSettings = gson.fromJson(settings_string, Settings.class);
			
			if(mSettings.Verbose) { System.out.println("Loaded settings:" + gson.toJson(mSettings)); } 
		}
		catch (Exception e) 
		{
			System.err.println("Couldn't load settings file " + settings_filename);
			e.printStackTrace();
			return;
		}
		
		try
		{
			// Print out all known serial ports
			if(mSettings.Verbose)
			{
		        java.util.Enumeration<CommPortIdentifier> portEnum = CommPortIdentifier.getPortIdentifiers();
				if (!portEnum.hasMoreElements())
				{
					System.out.println("No serial ports detected.");
				}
				else
				{
			        while ( portEnum.hasMoreElements() ) 
			        {
			            CommPortIdentifier portIdentifier = portEnum.nextElement();
			            System.out.println(portIdentifier.getName());
			        }
				}
			}
			
			// Open serial port
			CommPortIdentifier comm_port_identifier = CommPortIdentifier.getPortIdentifier(mSettings.SerialPortDescriptor);
			if(comm_port_identifier.isCurrentlyOwned())
			{
				System.err.println("Serial port " + mSettings.SerialPortDescriptor + " in use; exiting.");
				System.exit(0);
			}
			CommPort comm_port = comm_port_identifier.open("RobotController", 2000);
			if(!(comm_port instanceof SerialPort))
			{
				System.err.println("Port " + mSettings.SerialPortDescriptor + " is not a serial port; exiting.");
				System.exit(0);
			}
			SerialPort serial_port = (SerialPort)comm_port;
			serial_port.setSerialPortParams(9600, SerialPort.DATABITS_8,
					SerialPort.STOPBITS_1, SerialPort.PARITY_NONE);
			InputStream serial_input_stream = serial_port.getInputStream();
			OutputStream serial_output_stream = serial_port.getOutputStream();
			(new Thread(new SerialReader(serial_input_stream))).start();
			
			// Open AMQP server
			// TODO: This code is more or less copy-paste from the
			// rabbitmq examples.  This means it's not really suited
			// for relicensing and really shouldn't be released without
			// taking care to respect their licensing.
			ConnectionFactory cf = new ConnectionFactory();
			cf.setHost(mSettings.AmqpServer);
			Connection connection = cf.newConnection();
			Channel channel = connection.createChannel();
			channel.exchangeDeclare(mSettings.MotorSetExchange, "fanout");
			String queue_name = channel.queueDeclare().getQueue();
			channel.queueBind(queue_name, mSettings.MotorSetExchange, "");
			QueueingConsumer consumer = new QueueingConsumer(channel);
			channel.basicConsume(queue_name, true, consumer);
			
			// Wait for and handle messages
			while(true)
			{
				QueueingConsumer.Delivery delivery = consumer.nextDelivery();
				String json_message = new String(delivery.getBody());
//				if(mSettings.Verbose) { System.out.println("Received message from relay server: " + json_message); }
				
				MotorControlMessage motor_set_msg = gson.fromJson(json_message, MotorControlMessage.class);
				
				//send
				for(int motor_num = 0; motor_num < 6; ++motor_num)
				{
					String msg = motor_set_msg.GetMotorControlMessage(motor_num);
//					if(mSettings.Verbose) { System.out.println("Sending to motor driver: " + msg); }
					if(mSettings.Verbose) { System.err.println(msg); }
					serial_output_stream.write(msg.getBytes());
					serial_output_stream.flush();
				}
				
				String msg = motor_set_msg.GetWatchdogMessage();
				if(mSettings.Verbose) { System.err.println(msg); }
				serial_output_stream.write(msg.getBytes());
				serial_output_stream.flush();
			}
		}
		catch (Exception e) 
		{
			e.printStackTrace();
		}
	}
	
	public class Settings
	{
		public String AmqpServer;
		public boolean Verbose;
		public String MotorSetExchange;
		public String SerialPortDescriptor;
	};

	public static class SerialReader implements Runnable
	{
		InputStream serial_input_stream;
		
		public SerialReader(InputStream in_stream)
		{
			serial_input_stream = in_stream;
		}
		
		@Override
		public void run()
		{
			// Read everthing we can and output to stdout
			byte[] buffer = new byte[255];
			try 
			{
				int len;
				while((len = serial_input_stream.read(buffer)) >= 0)
				{
					if(len > 0)
					{
						System.out.print(new String(buffer, 0, len));
					}
				}
			}
			catch (IOException e) 
			{
				e.printStackTrace();
			}
			System.out.println("Reader thread exiting.");
		}
		
	}
}
