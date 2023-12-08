using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Media;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using SpinnakerNET;
using SpinnakerNET.GenApi;
using System.Threading;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using System.IO;
using System.Windows.Forms;
using Microsoft.VisualBasic;
using System.Drawing.Imaging;

namespace CAMERASPINNAKER
{
    public partial class Form1 : Form
    {
        ManagedSystem spinnakerSystem;
        ManagedCameraList camList;
        System.Windows.Forms.ComboBox comboBox;
        System.Windows.Forms.Button capture;
        List<PictureBox> pbl = new List<PictureBox>();
        string location = @"C:\Capture\";
        string extension = ".jpg";
        System.Windows.Forms.ComboBox ExtensionscomboBox;
        System.Windows.Forms.Button selectFolder;
        public static Dictionary<IManagedCamera, bool> CameraList { get; set; }

        IManagedCamera cam;

        // Configure image event handlers
        List<ImageEventListener> imageEventsList = new List<ImageEventListener>();
        List<SemaphoreSlim> semalist;

        ImageEventListener myImageEventListener = null;
        SemaphoreSlim sema = new SemaphoreSlim(1);
        public Form1()
        {
            InitializeComponent();
            //this.StopStreamButton.Enabled = false;
        }
        private void InitializeSpinnaker()
        {
            spinnakerSystem = new ManagedSystem();
            // Print out current library version
            LibraryVersion spinVersion = spinnakerSystem.GetLibraryVersion();
            Console.WriteLine(
                "Spinnaker library version: {0}.{1}.{2}.{3}",
                spinVersion.major,
                spinVersion.minor,
                spinVersion.type,
                spinVersion.build);
        }
        class ImageEventListener : ManagedImageEventHandler
        {
            private string deviceSerialNumber;
            public int imageCnt;
            List<IManagedImage> ConvertList;
            IManagedImageProcessor processor;

            PictureBox imageEventPictureBox;
            SemaphoreSlim displayMutex;

            // The constructor retrieves the serial number and initializes the
            // image counter to 0.
            public ImageEventListener(IManagedCamera cam, ref PictureBox pictureBoxInput, ref SemaphoreSlim displayMutexInput)
            {
                // Double buffer
                ConvertList = new List<IManagedImage>();
                ConvertList.Add(new ManagedImage());
                ConvertList.Add(new ManagedImage());

                // Initialize image counter to 0
                imageCnt = 0;
                imageEventPictureBox = pictureBoxInput;
                displayMutex = displayMutexInput;
                deviceSerialNumber = "";

                // Retrieve device serial number
                INodeMap nodeMap = cam.GetTLDeviceNodeMap();
                IString iDeviceSerialNumber = nodeMap.GetNode<IString>("DeviceSerialNumber");
                if (iDeviceSerialNumber != null && iDeviceSerialNumber.IsReadable)
                {
                    deviceSerialNumber = iDeviceSerialNumber.Value;
                }
                Console.WriteLine("ImageEvent initialized for camera serial: {0}", deviceSerialNumber);
                processor = new ManagedImageProcessor();
            }

            ~ImageEventListener()
            {
                //Cleanup double buffer
                if (ConvertList != null)
                {
                    foreach (var item in ConvertList)
                    {
                        item.Dispose();
                    }
                }
            }
            override protected void OnImageEvent(ManagedImage image)
            {

                if (image.FrameID % 100 == 0)
                {
                    Console.WriteLine("Image event! (We are only printing every 100 counts..) FrameID:{0}, ImageStatus:{1}",
                       image.FrameID,
                       image.ImageStatus.ToString()
                       );
                }
                IManagedImage doubleBufferImage = ConvertList[(int)image.FrameID % 2];


                if (displayMutex.Wait(TimeSpan.Zero))
                {
                    try
                    {
                        using (IManagedImage convertedImage = processor.Convert(image, PixelFormatEnums.BGR8))
                        {
                            doubleBufferImage.DeepCopy(convertedImage);
                            //imageEventPictureBox.SizeMode = PictureBoxSizeMode.Zoom;
                            imageEventPictureBox.Image = doubleBufferImage.bitmap;
                            
                        }
                    }
                    catch (SpinnakerException ex)
                    {
                        Console.WriteLine("Exception: {0} ", ex);
                    }
                    finally
                    {
                        image.Release();
                        displayMutex.Release();
                    }
                }
                else
                {

                    Console.WriteLine("Not processing FrameID: {0} as previous one is still being processed", image.FrameID);
                    image.Release();
                }
            }
        }
        int ConfigureImageEvents(IManagedCamera cam, ref ImageEventListener eventListenerToConfig, PictureBox pb)
        {
            int result = 0;
            try
            {
                eventListenerToConfig = new ImageEventListener(cam, ref pb, ref sema);
                cam.RegisterEventHandler(eventListenerToConfig);
                Console.WriteLine("**Image Event Handler Registered**");
            }
            catch (SpinnakerException ex)
            {
                Console.WriteLine("Error: {0}", ex.Message);
                result = -1;
            }
            return result;
        }


        static int ConfigureGVCPHeartbeat(IManagedCamera cam, bool enable)
        {
            // Retrieve TL device nodemap and print device information
            INodeMap nodeMapTLDevice = cam.GetTLDeviceNodeMap();

            // Retrieve GenICam nodemap
            INodeMap nodeMap = cam.GetNodeMap();

            IEnum iDeviceType = nodeMapTLDevice.GetNode<IEnum>("DeviceType");
            IEnumEntry iDeviceTypeGEV = iDeviceType.GetEntryByName("GigEVision");
            // We first need to confirm that we're working with a GEV camera
            if (iDeviceType != null && iDeviceType.IsReadable)
            {
                if (iDeviceType.Value == iDeviceTypeGEV.Value)
                {
                    if (enable)
                    {
                        Console.WriteLine("Resetting heartbeat");
                    }
                    else
                    {
                        Console.WriteLine("Disabling heartbeat");
                    }
                    IBool iGEVHeartbeatDisable = nodeMap.GetNode<IBool>("GevGVCPHeartbeatDisable");
                    if (iGEVHeartbeatDisable == null || !iGEVHeartbeatDisable.IsWritable)
                    {
                        Console.WriteLine(
                            "Unable to disable heartbeat on camera. Continuing with execution as this may be non-fatal...");
                    }
                    else
                    {
                        iGEVHeartbeatDisable.Value = enable;

                        if (!enable)
                        {
                            Console.WriteLine("         Heartbeat timeout has been disabled for this run. This allows pausing ");
                            Console.WriteLine("         and stepping through  code without camera disconnecting due to a lack ");
                            Console.WriteLine("         of a heartbeat register read.");
                        }
                        else
                        {
                            Console.WriteLine("         Heartbeat timeout has been enabled.");
                        }
                        Console.WriteLine();
                    }
                }
            }
            else
            {
                Console.WriteLine("Unable to access TL device nodemap. Aborting...");
                return -1;
            }

            return 0;
        }

        private void ConfigureCamera(IManagedCamera cam)
        {
            INodeMap snodeMap = cam.GetTLStreamNodeMap();
            IEnum iHandlingMode = snodeMap.GetNode<IEnum>("StreamBufferHandlingMode");
            if (iHandlingMode != null && iHandlingMode.IsWritable && iHandlingMode.IsReadable)
            {
                // Default is oldest first
                IEnumEntry iHandlingModeEntry = iHandlingMode.GetEntryByName("NewestOnly");
                iHandlingMode.Value = iHandlingModeEntry.Value;
                Console.WriteLine("Camera Serial: {0} buffer handling mode set to NewestOnly", cam.DeviceSerialNumber);
            }
        }
        private void ConnectCamera(IManagedCamera c, PictureBox pb, ImageEventListener imageEventListener)
        {
            // Retrieve list of cameras from the system
            //camList = spinnakerSystem.GetCameras();
                ConfigureCamera(c);
                ConfigureImageEvents(c, ref imageEventListener, pb);
                ConfigureGVCPHeartbeat(c, false);
                c.BeginAcquisition();
                Console.WriteLine("Acquisition Started");
            }
  

        private void CleanupSpinnaker()
        {
            try
            {
                // Clear camera list before releasing system
                if (cam.IsValid())
                {
                    cam.UnregisterEventHandler(myImageEventListener);
                    cam.EndAcquisition();
                    Console.WriteLine("Stream Stopped");

                    // This enables heartbeat again
                    ConfigureGVCPHeartbeat(cam, true);
                    cam.DeInit();
                    camList.Clear();
                }
                // Release system
                spinnakerSystem.Dispose();
            }
            catch (SpinnakerException ex)
            {
                Console.WriteLine("Exception during cleanup: {0}", ex);
            }

        }


        private void StreamButton_Click(object sender, EventArgs e)
        {

        }

        private void StopStreamButton_Click(object sender, EventArgs e)
        {
            // this.StopStreamButton.Enabled = false;
            // CleanupSpinnaker();
            //this.StreamButton.Enabled = true;
        }

        public void initialiseCameras()
        {
            CameraList = new Dictionary<IManagedCamera, bool>();
            foreach (IManagedCamera cam in camList)
            {
                IManagedCamera managedPGCamera = cam;
                // Initialize camera

                // Run 10 tries of initialization
                bool initSuccess = false;
                for (int tryNum = 0; !initSuccess && tryNum < 10; tryNum++)
                {
                    try
                    {
                        managedPGCamera.Init();
                        initSuccess = true;
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Initialization failed message below");
                        Console.WriteLine(e.Message);
                        Console.WriteLine("Initialization failed stack track below");
                        Console.WriteLine(e.StackTrace);
                        initSuccess = false;
                        Console.WriteLine("End of initialization error message");
                    }
                }

                if (managedPGCamera.IsInitialized())
                {
                    Console.WriteLine("Camera " + cam + " initialized successfully");
                    CameraList.Add(cam, true);
                }
                else
                {
                    CameraList.Add(cam, false);
                }
         
            }
        }
       
        private void Form1_Load(object sender, EventArgs e)
        {
            InitializeSpinnaker();
            camList = spinnakerSystem.GetCameras();
            initialiseCameras();
            IManagedCamera camera;
            comboBox = new System.Windows.Forms.ComboBox();
            comboBox.Location = new Point(1200, 920);
            comboBox.Size = new Size(220, 96);
            comboBox.Name = "cameraCombobox";
            comboBox.SelectedIndexChanged += comboBox_SelectedIndexChanged;
            ExtensionscomboBox = new System.Windows.Forms.ComboBox();
            ExtensionscomboBox.Location = new Point(1620, 920);
            ExtensionscomboBox.Size = new Size(80, 38);
            ExtensionscomboBox.Items.Add(".jpg");
            ExtensionscomboBox.Items.Add(".png");
            ExtensionscomboBox.SelectedIndex = 0;
            ExtensionscomboBox.SelectedIndexChanged += extensionscomboBox_SelectedIndexChanged;
            capture = new System.Windows.Forms.Button();
            capture.Location = new Point(1720, 915);
            capture.Size = new Size(120, 38);
            capture.Text = "Capture";
            capture.Click += captureCamera;
            selectFolder = new System.Windows.Forms.Button();
            selectFolder.Location = new Point(1460, 915);
            selectFolder.Size = new Size(120, 38);
            selectFolder.Text = "Change Folder";
            selectFolder.Click += ChangeFolderPath;
            this.Controls.Add(comboBox);
            this.Controls.Add(capture);
            this.Controls.Add(ExtensionscomboBox);
            this.Controls.Add(selectFolder);
            imageEventsList = new List<ImageEventListener>();

            int l = 0;
            int w = 0;
            string itemname = "";
            for (int i = 0; i < camList.Count; i++)
            {
                itemname = "cam" + (i+1);
                comboBox.Items.Add(itemname);
                camera = camList[i];

               // c = camList[i];
                //ConnectCamera(camList[i]);
                l = (i % 4) * 420;

                if (i < 4)
                {

                    w = 0;
                }
                else
                    w = 500;

                createCameraLabel(l, w, i,camera);
            }
        }
        public void createCameraLabel(int l, int w, int i, IManagedCamera camera)
        {
            Panel p = new Panel();
            PictureBox pb = new PictureBox();
            System.Windows.Forms.Button available = new System.Windows.Forms.Button();
            System.Windows.Forms.Button notAvailable = new System.Windows.Forms.Button();
            
            
            p.Location = new Point(l, w);
            p.Size = new Size(480, 450);

            pb.Location = new Point(2, 2);
            pb.Size = new Size(400, 400);
            //pb.SizeMode = PictureBoxSizeMode.Zoom;

            pbl.Add(pb);

            available.Size = new Size(16, 16);
            available.Location = new Point(175, 420);
            notAvailable.Location = new Point(200, 420);
            notAvailable.Size = new Size(16, 16);
            if (CameraList[camera] == true)
                available.BackColor = Color.Green;
            else
                notAvailable.BackColor = Color.Red;

            if(CameraList[camera] == true)
            {
                imageEventsList.Add(new ImageEventListener(camera,ref pb,ref sema));

                ConnectCamera(camera, pb, imageEventsList[i]);

            }
            else
            {
                imageEventsList[i] = null;
            }

                this.Controls.Add(p);
            p.Controls.Add(pb);
            p.Controls.Add(available);
            p.Controls.Add(notAvailable);
          
        }
        private void comboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            int index = comboBox.SelectedIndex;
            if (CameraList[camList[index]])
                capture.Enabled = true;
            else
                capture.Enabled = false;
        }

        private void captureCamera(object sender, EventArgs e)
        {
            Image img;
            int index = comboBox.SelectedIndex;

            // string filename = String.Format("cam_{0}_{1}",index,DateTime.UtcNow.ToString("yyyy-MM-dd HH-mm-ss"));
            // pbl[index].Image.Save(filename,ImageFormat.Jpeg);
            //write pucture save image
            //  string foldername = @"C:\capture\";
            //string filename = String.Format("cam_{0}_{1}", index, DateTime.UtcNow.ToString("yyyy-MM-dd HH-mm-ss"));

            //string path = Path.Combine(Environment.GetFolderPath(foldername, filename));
            // string folderName = @"Capture\" + DateTime.Now.ToString("yyMMddhhmmss");
            //string folderName = @"C:\Capture\";
            string folderName = location;
            string filename = "Cam_" + index + DateTime.Now.ToString("yyMMddhhmmss") + extension;
            string path = folderName + filename;
            if (extension == ".jpg")
                pbl[index].Image.Save(path,ImageFormat.Jpeg);
            else
                pbl[index].Image.Save(path, ImageFormat.Png);
        }
        private void extensionscomboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            extension = ExtensionscomboBox.SelectedItem.ToString();
        }
        private void ChangeFolderPath(object sender, EventArgs e)
        {
            FolderBrowserDialog diag = new FolderBrowserDialog();
            if (diag.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                location = diag.SelectedPath + @"\";
            }
        }
    }


}
