/*
 * A tank controller GUI, writted by John Walthour in August 2011.
 * I've blatantly copied some code from the SlimDX demos, and in keeping with their
 * wishes have reproduced their license below.
 * 
 * /

/*
* Copyright (c) 2007-2009 SlimDX Group
* 
* Permission is hereby granted, free of charge, to any person obtaining a copy
* of this software and associated documentation files (the "Software"), to deal
* in the Software without restriction, including without limitation the rights
* to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
* copies of the Software, and to permit persons to whom the Software is
* furnished to do so, subject to the following conditions:
* 
* The above copyright notice and this permission notice shall be included in
* all copies or substantial portions of the Software.
* 
* THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
* IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
* FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
* AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
* LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
* OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
* THE SOFTWARE.
*/
using System;
using System.Globalization;
using SlimDX;
using SlimDX.DirectInput;
using System.Collections.Generic;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

using Newtonsoft.Json;
using RabbitMQ.Client;

namespace TankControlGui
{

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public enum POV_HAT_DIR
        {
            N = 0,
            NE = 4500,
            E = 9000,
            SE = 13500,
            S = 18000,
            SW = 22500,
            W = 27000,
            NW = 31500,
            NONE = -1,
        };
        Joystick joystick;
        System.Windows.Threading.DispatcherTimer timer = new System.Windows.Threading.DispatcherTimer();
        JoystickState state = new JoystickState();
        POV_HAT_DIR pov_state = 0;
        bool[] button_state;
        int control_x = 0;
        int control_y = 0;
        int control_z = 0;
        int control_throttle = 0;

        /// <summary>How long a button press is effective for </summary>
        TimeSpan BUTTON_EFFECTIVE_TIME = new System.TimeSpan(0, 0, 0, 1);
        /// <summary>
        /// The joystick power a non-joystick button press is equivalent to, in joystick units (-1023 to +1023)
        /// </summary>
        const int BUTTON_POWER = 512;
        /// <summary> Direct the robot controller to nullify motion if 
        /// another message isn't received in this amount of time.
        /// </summary>
        const int MOTOR_SET_EFFECTIVE_TIME_MS = 300;
        const int JOYSTICK_XY_DEADBAND = 50;
        const int JOYSTICK_Z_DEADBAND = 300;
        const int JOYSTICK_THROTTLE_DEADBAND = 300;
        const int TURRET_VERT_SPEED_SLOW = 64;
        const int TURRET_VERT_SPEED_MED = 128;
        const int TURRET_VERT_SPEED_FAST = 255;

        // Time each button is effective until
        DateTime w_button_effective_until = DateTime.Now - new TimeSpan(1, 0, 0);
        DateTime s_button_effective_until = DateTime.Now - new TimeSpan(1, 0, 0);
        DateTime a_button_effective_until = DateTime.Now - new TimeSpan(1, 0, 0);
        DateTime d_button_effective_until = DateTime.Now - new TimeSpan(1, 0, 0);

        // Motor numbers
        const int RIGHT_DRIVE_MOTOR_NUM = 0;
        const int LEFT_DRIVE_MOTOR_NUM = 1;
        const int TURRET_HORZ_MOTOR_NUM = 2;
        const int TURRET_VERT_MOTOR_NUM = 3;
        const int WEAPON_0_MOTOR_NUM = 4;
        const int WEAPON_1_MOTOR_NUM = 5;

        #region Motor values and directions
        int left_tread_pwr = 0;
        bool left_tread_fwd = true;
        int right_tread_pwr = 0;
        bool right_tread_fwd = true;

        int turret_vert_pwr = 0;
        bool turret_vert_fwd = true;
        int turret_horiz_pwr = 0;
        bool turret_horiz_fwd = true;

        int weapon1_pwr = 0;
        bool weapon1_fwd = true;
        int weapon2_pwr = 0;
        bool weapon2_fwd = true;

        #endregion

        #region Connection-related

        private IModel channel = null;

        #endregion

        public MainWindow()
        {
            InitializeComponent();
            CreateDevice();

            timer.Interval = new System.TimeSpan(0, 0, 0, 0, 100);
            timer.Tick += new System.EventHandler(timer_Tick);
            timer.Start();
            LogMessage("Initialized.");
        }

        void timer_Tick(object sender, System.EventArgs e)
        {
            ComputeInputs();
            ComputeDriveFormula();
            UpdateControls();
            if (!(bool)checkBoxManualSendOnly.IsChecked)
            { SendMotorControlMessage(); }
        }

        private void ComputeInputs()
        {
            // Pull from joystick
            if (joystick != null)
            {
                joystick.Poll();
                state = joystick.GetCurrentState();
                pov_state = (POV_HAT_DIR)state.GetPointOfViewControllers()[0];
                button_state = state.GetButtons();
                control_x = state.X;
                control_y = state.Y;
                control_z = state.RotationZ;
                control_throttle = state.GetSliders()[0];
                if (control_x > 0 && control_x < JOYSTICK_XY_DEADBAND) { control_x = 0; }
                if (control_x < 0 && control_x > -JOYSTICK_XY_DEADBAND) { control_x = 0; }
                if (control_y > 0 && control_y < JOYSTICK_XY_DEADBAND) { control_y = 0; }
                if (control_y < 0 && control_y > -JOYSTICK_XY_DEADBAND) { control_y = 0; }
                if (control_z > 0 && control_z < JOYSTICK_Z_DEADBAND) { control_z = 0; }
                if (control_z < 0 && control_z > -JOYSTICK_Z_DEADBAND) { control_z = 0; }
            }
            else
            {
                state = new JoystickState();
                button_state = new bool[] { false, false, false, false };
                control_x = 0;
                control_y = 0;
                pov_state = POV_HAT_DIR.NONE;
            }

            // Turret
            switch (pov_state)
            {
                case POV_HAT_DIR.NW:
                    turret_vert_pwr = TURRET_VERT_SPEED_SLOW;
                    turret_vert_fwd = true;// down
                    turret_horiz_pwr = TURRET_VERT_SPEED_SLOW;
                    turret_horiz_fwd = true;// left
                    break;
                case POV_HAT_DIR.N:
                    turret_vert_pwr = TURRET_VERT_SPEED_MED;
                    turret_vert_fwd = true;// down
                    turret_horiz_pwr = 0;
                    turret_horiz_fwd = false;// right
                    break;
                case POV_HAT_DIR.NE:
                    turret_vert_pwr = TURRET_VERT_SPEED_SLOW;
                    turret_vert_fwd = true;// down
                    turret_horiz_pwr = TURRET_VERT_SPEED_SLOW;
                    turret_horiz_fwd = false;// right
                    break;
                case POV_HAT_DIR.E:
                    turret_vert_pwr = 0;
                    turret_vert_fwd = true;// down
                    turret_horiz_pwr = TURRET_VERT_SPEED_MED;
                    turret_horiz_fwd = false;// right
                    break;
                case POV_HAT_DIR.SE:
                    turret_vert_pwr = TURRET_VERT_SPEED_SLOW;
                    turret_vert_fwd = false;// up
                    turret_horiz_pwr = TURRET_VERT_SPEED_SLOW;
                    turret_horiz_fwd = false;// right
                    break;
                case POV_HAT_DIR.S:
                    turret_vert_pwr = TURRET_VERT_SPEED_MED;
                    turret_vert_fwd = false;// up
                    turret_horiz_pwr = 0;
                    turret_horiz_fwd = false;// right
                    break;
                case POV_HAT_DIR.SW:
                    turret_vert_pwr = TURRET_VERT_SPEED_SLOW;
                    turret_vert_fwd = false;// up
                    turret_horiz_pwr = TURRET_VERT_SPEED_SLOW;
                    turret_horiz_fwd = true;// left
                    break;
                case POV_HAT_DIR.W:
                    turret_vert_pwr = 0;
                    turret_vert_fwd = true;// down
                    turret_horiz_pwr = TURRET_VERT_SPEED_MED;
                    turret_horiz_fwd = true;// left
                    break;

                default:
                    turret_vert_pwr = 0;
                    turret_horiz_pwr = 0;
                    break;
            }

            // Normal weapons
            /*if (button_state[0])
            {
                weapon1_fwd = true;
                weapon1_pwr = 255;
            }
            else
            {
                weapon1_fwd = true;
                weapon1_pwr = 0;
            }
            if (button_state[1])
            {
                weapon2_fwd = false;
                weapon2_pwr = 255;
            }
            else
            {
                weapon2_fwd = false;
                weapon2_pwr = 0;
            }*/

            // Lighting unit
            int light_power = (int)((-control_throttle + 1000.0) * (255.0 / 2000.0));
            switch (comboBoxLightType.SelectedIndex)
            {
                case 0: // white
                default:
                    weapon1_fwd = false;
                    weapon1_pwr = light_power;
                    weapon2_pwr = 0;
                    break;
                case 1: // IR
                    weapon2_fwd = false;
                    weapon2_pwr = light_power;
                    weapon1_pwr = 0;
                    break;
                case 2: // UV
                    weapon1_fwd = true;
                    weapon1_pwr = light_power;
                    weapon2_pwr = 0;
                    break;
                case 3: // Laser
                    weapon2_fwd = true;
                    weapon2_pwr = light_power;
                    weapon1_pwr = 0;
                    break;
            }

            // Button control (overrides joystick)
            if (DateTime.Now < w_button_effective_until)
            { control_y = -BUTTON_POWER; }
            else if (DateTime.Now < s_button_effective_until)
            { control_y = BUTTON_POWER; }
            if (DateTime.Now < a_button_effective_until)
            { control_x = -BUTTON_POWER; }
            else if (DateTime.Now < d_button_effective_until)
            { control_x = BUTTON_POWER; }
        }

        private void buttonStill_Click(object sender, RoutedEventArgs e)
        {
            w_button_effective_until = DateTime.Now - new TimeSpan(1, 0, 0);
            s_button_effective_until = DateTime.Now - new TimeSpan(1, 0, 0);
            a_button_effective_until = DateTime.Now - new TimeSpan(1, 0, 0);
            d_button_effective_until = DateTime.Now - new TimeSpan(1, 0, 0);
        }

        private void UpdateControls()
        {
            progressBarLeftTreadFwd.Value = left_tread_fwd ? left_tread_pwr : 0;
            progressBarLeftTreadRev.Value = !left_tread_fwd ? left_tread_pwr : 0;
            progressBarRightTreadFwd.Value = right_tread_fwd ? right_tread_pwr : 0;
            progressBarRightTreadRev.Value = !right_tread_fwd ? right_tread_pwr : 0;
            progressBarTurretHorizFwd.Value = turret_horiz_fwd ? turret_horiz_pwr : 0;
            progressBarTurretHorizRev.Value = !turret_horiz_fwd ? turret_horiz_pwr : 0;
            progressBarTurretVertFwd.Value = turret_vert_fwd ? turret_vert_pwr : 0;
            progressBarTurretVertRev.Value = !turret_vert_fwd ? turret_vert_pwr : 0;
            progressBarWeapon1Fwd.Value = weapon1_fwd ? weapon1_pwr : 0;
            progressBarWeapon1Rev.Value = !weapon1_fwd ? weapon1_pwr : 0;
            progressBarWeapon2Fwd.Value = weapon2_fwd ? weapon2_pwr : 0;
            progressBarWeapon2Rev.Value = !weapon2_fwd ? weapon2_pwr : 0;

            progressBarJoystickXFwd.Value = (control_x > 0) ? control_x : 0;
            progressBarJoystickXRev.Value = (control_x < 0) ? -control_x : 0;
            progressBarJoystickYFwd.Value = (control_y < 0) ? -control_y : 0;
            progressBarJoystickYRev.Value = (control_y > 0) ? control_y : 0;
            progressBarJoystickThrottleFwd.Value = (control_throttle < 0) ? -control_throttle : 0;
            progressBarJoystickThrottleRev.Value = (control_throttle > 0) ? control_throttle : 0;

            labelXInput.Content = "x:" + control_x;
            labelYInput.Content = "y:" + control_y;
            labelZInput.Content = "z:" + control_z;
        }



        private void ComputeDriveFormula()
        {
            // Start in joystick units (-1023 to +1023)

            int x_direction;
            //if (control_y < 0)
            { // Going forwards - normal controls
                x_direction = 1;
            }
           // else
            { // Going backwards - flip the x 
            //    x_direction = -1;
            }
            left_tread_pwr  = -control_y + x_direction * control_x;
            right_tread_pwr = -control_y - x_direction * control_x;

            // Scale and convert (0 to +255 & direction)
            left_tread_fwd = left_tread_pwr > 0;
            left_tread_pwr = (int)(left_tread_pwr * 255.0 / 1000.0);
            left_tread_pwr *= left_tread_fwd ? 1 : -1;
            right_tread_fwd = right_tread_pwr > 0;
            right_tread_pwr = (int)(right_tread_pwr * 255.0 / 1000.0);
            right_tread_pwr *= right_tread_fwd ? 1 : -1;

            //turret_horiz_fwd = control_z < 0;
            //turret_horiz_pwr = (int)(Math.Abs(control_z) * 255.0 / 1000.0);

            left_tread_pwr = Math.Min(left_tread_pwr, 255);
            right_tread_pwr = Math.Min(right_tread_pwr, 255);
            turret_horiz_pwr = Math.Min(turret_horiz_pwr, 255);
        }

        private void buttonConnect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ConnectionFactory cf = new ConnectionFactory();
                cf.Uri = "amqp://" + textBoxIpAddress.Text;
                IConnection connection = cf.CreateConnection();
                channel = connection.CreateModel();
                connection.AutoClose = true;
                channel.ExchangeDeclare("motor_set", "fanout");
                LogMessage("Connected to " + "amqp://" + textBoxIpAddress.Text);
            }
            catch (Exception ex)
            {
                LogMessage(ex.ToString());
            }
        }

        void SendMotorControlMessage()
        {
            MotorControlMessage ctrl_msg = new MotorControlMessage();
            ctrl_msg.EffectiveTimeMs = MOTOR_SET_EFFECTIVE_TIME_MS;
            ctrl_msg.Power = new short[6];
            ctrl_msg.Fwd = new Boolean[6];

            ctrl_msg.Power[LEFT_DRIVE_MOTOR_NUM] = (short)left_tread_pwr;
            ctrl_msg.Fwd[LEFT_DRIVE_MOTOR_NUM] = left_tread_fwd;
            ctrl_msg.Power[RIGHT_DRIVE_MOTOR_NUM] = (short)right_tread_pwr;
            ctrl_msg.Fwd[RIGHT_DRIVE_MOTOR_NUM] = right_tread_fwd;

            ctrl_msg.Power[TURRET_VERT_MOTOR_NUM] = (short)turret_vert_pwr;
            ctrl_msg.Fwd[TURRET_VERT_MOTOR_NUM] = turret_vert_fwd;
            ctrl_msg.Power[TURRET_HORZ_MOTOR_NUM] = (short)turret_horiz_pwr;
            ctrl_msg.Fwd[TURRET_HORZ_MOTOR_NUM] = turret_horiz_fwd;

            ctrl_msg.Power[WEAPON_0_MOTOR_NUM] = (short)weapon1_pwr;
            ctrl_msg.Fwd[WEAPON_0_MOTOR_NUM] = weapon1_fwd;
            ctrl_msg.Power[WEAPON_1_MOTOR_NUM] = (short)weapon2_pwr;
            ctrl_msg.Fwd[WEAPON_1_MOTOR_NUM] = weapon2_fwd;

            if (channel != null)
            {
                String msg_str = JsonConvert.SerializeObject(ctrl_msg);
                LogMessage("Sending " + msg_str);
                channel.BasicPublish("motor_set", "", null, Encoding.UTF8.GetBytes(msg_str));
            }
        }

        void CreateDevice()
        {
            // make sure that DirectInput has been initialized
            DirectInput dinput = new DirectInput();

            // search for devices
            foreach (DeviceInstance device in dinput.GetDevices(DeviceClass.GameController, DeviceEnumerationFlags.AttachedOnly))
            {
                // create the device
                try
                {
                    joystick = new Joystick(dinput, device.InstanceGuid);
                    //joystick.SetCooperativeLevel(this, CooperativeLevel.Exclusive | CooperativeLevel.Foreground);
                    break;
                }
                catch (DirectInputException)
                {
                }
            }

            if (joystick == null)
            {
                LogMessage("There are no joysticks attached to the system.");
                LogMessage("Restart application after connecting stick.");
                return;
            }

            foreach (DeviceObjectInstance deviceObject in joystick.GetObjects())
            {
                if ((deviceObject.ObjectType & ObjectDeviceType.Axis) != 0)
                    joystick.GetObjectPropertiesById((int)deviceObject.ObjectType).SetRange(-1000, 1000);
            }

            // acquire the device
            joystick.Acquire();
        }

        void ReleaseDevice()
        {
            if (joystick != null)
            {
                joystick.Unacquire();
                joystick.Dispose();
            }
            joystick = null;
        }

        private void buttonSend_Click(object sender, RoutedEventArgs e)
        {
            SendMotorControlMessage();
        }

        private void buttonDisconnect_Click(object sender, RoutedEventArgs e)
        {
            channel.Close();
            channel.Dispose();
            channel = null;
            LogMessage("Disconnected");
        }

        private void buttonW_Click(object sender, RoutedEventArgs e)
        {
            w_button_effective_until = DateTime.Now + BUTTON_EFFECTIVE_TIME;
        }

        private void buttonA_Click(object sender, RoutedEventArgs e)
        {
            a_button_effective_until = DateTime.Now + BUTTON_EFFECTIVE_TIME;
        }

        private void buttonS_Click(object sender, RoutedEventArgs e)
        {
            s_button_effective_until = DateTime.Now + BUTTON_EFFECTIVE_TIME;
        }

        private void buttonD_Click(object sender, RoutedEventArgs e)
        {
            d_button_effective_until = DateTime.Now + BUTTON_EFFECTIVE_TIME;
        }

        private void Window_Closed(object sender, System.EventArgs e)
        {
            buttonDisconnect_Click(null, null);
            Application.Current.Shutdown();
        }

        private void LogMessage(String msg)
        {
            textBoxMessageLog.AppendText(DateTime.Now.ToLongTimeString() + " ");
            textBoxMessageLog.AppendText(msg);
            textBoxMessageLog.AppendText("\n");
            textBoxMessageLog.ScrollToEnd();
        }
    }
}
