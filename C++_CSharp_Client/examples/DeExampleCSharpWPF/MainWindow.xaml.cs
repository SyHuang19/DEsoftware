﻿using System;
using System.Collections.Generic;
using System.Collections;
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
using DeInterface;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO.MemoryMappedFiles;
using System.IO;
using System.Runtime.InteropServices;
using System.Net;
using HDF5DotNet;
using CSharpExample1;
using System.Timers;
using System.Drawing;
using System.Windows.Forms;

namespace DeExampleCSharpWPF
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private DeInterfaceNET _deInterface;
        private bool _liveModeEnabled;
        //private LiveModeView _liveView;   LiveViewWindow no longer a separate window
        private bool _closing;
        UInt16[] m_image_local;
        static Semaphore semaphore;
        Queue<UInt16[]> m_imageQueue = new Queue<ushort[]>();
        public int numpos;
        public int height;
        public int width;

        public ObservableCollection<Property> CameraProperties { get; private set; }

        public MainWindow()
        {
            InitializeComponent();
        }

        // used to close main window
        private void Window_Closing(object sender, CancelEventArgs e)
        {
            
        }

        // Get Image Transfer Mode      
        private ImageTransfer_Mode GetImageTransferMode()
        {
            string strHostName = Dns.GetHostName();
            IPHostEntry ipEntry = Dns.GetHostEntry(strHostName);

            bool bFind = false;
            foreach (IPAddress ipaddr in ipEntry.AddressList)
            {
                if (ipaddr.ToString() == IPAddr.Text.Trim())
                {
                    bFind = true;
                    break;
                }
            }
            if (!bFind && IPAddr.Text.Trim() != "127.0.0.1")
                return ImageTransfer_Mode.ImageTransfer_Mode_TCP;

            /*
             *  determine whether it is TCP mode or Memory map mode
             */
            using (MemoryMappedFile mmf = MemoryMappedFile.OpenExisting("ImageFileMappingObject"))
            {
                using (MemoryMappedViewAccessor viewAccessor = mmf.CreateViewAccessor())
                {
                    
                    int imageSize = Marshal.SizeOf((typeof(Mapped_Image_Data_)));
                    var imageDate = new Mapped_Image_Data_();
                    viewAccessor.Read(0, out imageDate);

                    if (imageDate.client_opened_mmf)
                        return ImageTransfer_Mode.ImageTransfer_Mode_MMF;                    
                }                
            }            

            return ImageTransfer_Mode.ImageTransfer_Mode_TCP;
        }

        private void btnConnect_Click(object sender, RoutedEventArgs e)
        {
            if (btnConnect.Content.ToString() == "Disconnect")
            {
/*                if (_liveModeEnabled)
                {
                    _liveView.Close();
                }*/
                _deInterface.close();
                cmbCameras.Items.Clear();
                cmbCameras.Text = "";
                btnConnect.Content = "Connect";
                slider_outerang.Value = 1;
            }
            else if (_deInterface.connect(IPAddr.Text, 48880, 48879))
            {

                DeError error = _deInterface.GetLastError();
                Console.WriteLine(error.Description);
                try
                {
                    //get the list of cameras for the combobox
                    List<String> cameras = new List<String>();
                    _deInterface.GetCameraNames(ref cameras);
                    cmbCameras.Items.Clear();
                    foreach (var camera in cameras)
                    {
                        cmbCameras.Items.Add(camera);
                    }
                    cmbCameras.SelectedIndex = 0;
                }
                catch (Exception exc)
                {
                    System.Windows.MessageBox.Show(exc.Message);
                }

                btnConnect.Content = "Disconnect";

                switch (GetImageTransferMode())
                { 
                    case ImageTransfer_Mode.ImageTransfer_Mode_MMF:
                        cmbTransport.SelectedIndex = 0;
                        break;
                    case ImageTransfer_Mode.ImageTransfer_Mode_TCP:
                        cmbTransport.SelectedIndex = 1;
                        break;
                    default:
                        break;
                }
                cmbTransport.IsEnabled = false;
                string xSize = "";
                string ySize = "";
                _deInterface.GetProperty("Image Size X", ref xSize);
                _deInterface.GetProperty("Image Size Y", ref ySize);
                PixelsX.Text = xSize;
                PixelsY.Text = ySize;
            }

    }


        // Get a 16 bit gray scale image from the server and return a BitmapSource

        private BitmapSource GetImage()
        {
            UInt16[] image;
            _deInterface.GetImage(out image);
            if (image == null)
            {
                DeError error = _deInterface.GetLastError();
                Console.WriteLine(error.Description);
                return null;
            }
            string xSize = "";
            string ySize = "";
            _deInterface.GetProperty("Image Size X", ref xSize);
            _deInterface.GetProperty("Image Size Y", ref ySize);
            width = Convert.ToInt32(xSize);
            height = Convert.ToInt32(ySize);
            int bytesPerPixel = (PixelFormats.Gray16.BitsPerPixel + 7) / 8;
            int stride = 4 * ((width * bytesPerPixel + 3) / 4);

            int length = width * height;
            ushort min = image[0];
            ushort max = image[0];
            for (int i = 1; i < length; i++)
            {
                if (image[i] < min) min = image[i];
                if (image[i] > max) max = image[i];
            }
            double gain = UInt16.MaxValue / Math.Max(max - min, 1);
            UInt16[] image16 = new UInt16[length];
            // load data into image16, 1D array for 2D image
            for (int i = 0; i < length; i++)
                image16[i] = (ushort)((image[i] - min) * gain);

            byte[] imageBytes = new byte[stride * height];


            return BitmapSource.Create(width, height, 96, 96, PixelFormats.Gray16, null, image16, stride);
        }


        private void btnGetImage_Click(object sender, RoutedEventArgs e)
        {
            SingleCapture();
        }

        // load mrc file that just acquired and do reconstrcution for BF/ABF
        public void LoadnRecon_Click(object sender, RoutedEventArgs e)
        {
            // if EnableDetector option is not checked, change it to ture and use default BF range
            if (EnableDetector.IsChecked == false)
            {
                EnableDetector.IsChecked = true;
                slider_innerang.Value = 0;
                slider_outerang.Value = 1;
            }
            // call function to load MRC file and do reconstruction
            ReadMRCfile();
        }

        // save the loaded mrc file to EMD format
        private void ResaveEMD_Click(object sender, RoutedEventArgs e)
        {
           // HDF5.InitializeHDF(numpos, height, width);
        }

        public void SingleCapture()
        {
            try
            {
                // old scheme of single acquisition in a new window
                /*ImageView imageView = new ImageView();
                imageView.image.Source = GetImage();    //return a BitmapSource
                imageView.Show();*/

                // image acquisition scheme adapted from live stream, display image in imagebox1
                InitializeWBmp(GetImage());
                Show();
                //InitializeWBmp(GetImage()); // initialize image in picture box
                                            //enable livemode on the server
            }
            catch (Exception exc)
            {
                System.Windows.MessageBox.Show(exc.Message);
            }
        }

        public void ReadMRCfile()
        {
            // start reading mrc file
            string path_string = "";
            string name_string = "";
            _deInterface.GetProperty("Autosave Directory", ref path_string);
            _deInterface.GetProperty("Autosave Frames - Previous Dataset Name", ref name_string);
            path_string = path_string.Replace("\\","/");
            string path = path_string + "/" + name_string + "_RawImages.mrc";

            using (var filestream = File.Open(@path, FileMode.Open))
            using (var binaryStream = new BinaryReader(filestream))
            {
                // read headers
                width = binaryStream.ReadInt32();
                height = binaryStream.ReadInt32();
                numpos = binaryStream.ReadInt32();
                int format = binaryStream.ReadInt32();
                for (var i = 0; i < 6; i++)    // the rest 6 integer numbers, int32, useless here
                {

                    Console.WriteLine(binaryStream.ReadInt32());
                }
                Console.WriteLine('\n');
                for (var i = 0; i < 12; i++)    // 12 floating numbers, single
                {
                    Console.WriteLine(binaryStream.ReadSingle());
                }
                Console.WriteLine('\n');
                for (var i = 0; i < 30; i++)    // 30 integer numbers, int32
                {
                    Console.WriteLine(binaryStream.ReadInt32());
                }
                Console.WriteLine('\n');
                for (var i = 0; i < 8; i++)    // 8 chars
                {
                    Console.WriteLine(binaryStream.ReadChar());
                }
                Console.WriteLine('\n');
                for (var i = 0; i < 2; i++)    // 2 integer numbers, int32
                {
                    Console.WriteLine(binaryStream.ReadInt32());
                }
                for (var i = 0; i < 10; i++)    // 10 strings
                {
                    Console.WriteLine(binaryStream.ReadChars(80));
                }

                // finish reading headers
                UInt16[,,] datacube = new UInt16[numpos, width, height];
                UInt16[] datacube_array = new UInt16[numpos * width * height];

                // 1D array created for reconstruction, two arrays probably cost a lot of RAM
                for (var ilayer = 0; ilayer < numpos; ilayer++)
                {
                    for (var iy = 0; iy < height; iy++)
                    {
                        for (var ix = 0; ix < width; ix++)
                        {
                            datacube_array[ilayer*width*height + iy*width + ix] = binaryStream.ReadUInt16();
                        }
                    }
                }
                // 3D array created for HDF5 save
                for (var ilayer = 0; ilayer < numpos; ilayer++)
                {
                    for (var iy = 0; iy < height; iy++)
                    {
                        for (var ix = 0; ix < width; ix++)
                        {
                            datacube[ilayer, iy, ix] = datacube_array[ilayer * width * height + iy * width + ix];
                        }
                    }
                }

                // start reconstruction and show reconstruction result if option enabled
                string StrX = null;
                string StrY = null;
                int px = 0, py = 0;

                // create H5 file with attributes and data
                H5FileId fileId = HDF5.InitializeHDF(numpos, width, height, datacube);


                PosX.Dispatcher.Invoke(
                    (ThreadStart)delegate { StrX = PosX.Text; }
                    );
                PosY.Dispatcher.Invoke(
                    (ThreadStart)delegate { StrY = PosX.Text; }
                    );

                if (Int32.TryParse(StrX, out px))
                {
                    if (Int32.TryParse(StrY, out py))
                    {
                        if (numpos == px * py)
                        {
                            Bitmap ReconBMP = new Bitmap(px, py);   // bitmap for recon purpose
                            UInt16[] recon = new UInt16[px * py]; // array for reconstrcution purpose
                            UInt16[] recon_scale = new UInt16[px * py]; // array for scaled reconstrcuction image
                            ushort sum = 0;
                            int min = 65535;
                            int max = 0;
                            recon_scale[0] = 255;
                            BitmapSource ReconBitmapSource = ConvertBitmapSource(ReconBMP); // convert bitmap to bitmapsource, then can be used to generate writable bitmap
                            InitializeWBmpRecon(ReconBitmapSource);
                            for (var iy = 0; iy < py; iy++)
                            {
                                for (var ix = 0; ix < px; ix++)
                                {
                                    UInt16[] imagelayer = ExtractArray(datacube_array, iy * px + ix, width, height);
                                    double innerang = 0;
                                    double outerang = 0;
                                    slider_innerang.Dispatcher.Invoke(
                                        (ThreadStart)delegate { innerang = slider_innerang.Value; }
                                        );
                                    slider_outerang.Dispatcher.Invoke(
                                        (ThreadStart)delegate { outerang = slider_outerang.Value; }
                                        );
                                    sum = IntegrateBitmap(imagelayer, width, height, innerang, outerang);
                                    recon[iy*px + ix] = sum;
                                    if (recon[iy * px + ix] < min) min = recon[iy * px + ix];
                                    if (recon[iy * px + ix] > max) max = recon[iy * px + ix]; //update max and min after recon array changed
                                    for (int i = 0; i < iy * px + ix; i++)
                                    {
                                        recon_scale[i] = (ushort)((recon[i] - min) * 255 / (max - min + 1));  // rescale with new max and min if scale changed
                                    }
                                }
                            }
                            this.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                _wBmpRecon.WritePixels(new Int32Rect(0, 0, px, py), recon_scale, px * 2, 0);

                            }));
                            
                        }
                    }
                }
            }
        }

        // function used to extract 2D layer from 3D datacube, used for 1D array saving scheme
        public UInt16[] ExtractArray(UInt16[] DataArray, int layernum, int width, int height)
        {
            UInt16[] layer = new UInt16[width*height];
            for (var iy = 0; iy < width; iy++)
            {
                for (var ix = 0; ix < height; ix++)
                {
                    layer[iy*width+ix] = DataArray[layernum*width*height+iy*width+ix];
                }
            }
            return layer;
        }

        // function used to extract 2D layer from 3D datacube
        public UInt16[,] ExtractLayer(UInt16[,,] DataCube, int layernum, int width, int height)
        {
            UInt16[,] layer = new UInt16[width, height];
            for (var iy = 0; iy < width; iy++)
            {
                for (var ix = 0; ix < height; ix++)
                {
                    layer[iy, ix] = DataCube[layernum,iy,ix];
                }
            }
            return layer;
        }

        public static void SaveClipboardImageToFile(string filePath)
        {
            var image = System.Windows.Clipboard.GetImage();
            using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                BitmapEncoder encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                encoder.Save(fileStream);
            }
        }

        // click to start using virtual detector to reconstrcut image
        private void button1_Click(object sender, RoutedEventArgs e)
        {
            SolidColorBrush strokeBrush = new SolidColorBrush(Colors.Red);
            strokeBrush.Opacity = .25d;
            InnerAngle.Visibility = Visibility.Visible;
            InnerAngle.Stroke = strokeBrush;
            InnerAngle.Height = 400;
            InnerAngle.StrokeThickness = InnerAngle.Height / 2;
            slider_outerang.Value = 1;
            slider_innerang.Value = 0;
        }

        // called by change on innerang slider, change the radius of inner angle ellipse
        private void changeinnerang(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            double innerang = slider_innerang.Value;
            InnerAngle.StrokeThickness = InnerAngle.Width / 2 * (1.0 - innerang);
            
        }

        // called by change on outerang slider, will simultaneously change ellipse thickness according to innerang
        private void changeouterang(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            double outerang = slider_outerang.Value;
            Thickness margin = InnerAngle.Margin;
            margin.Top = 7.0 + 200.0 * (1.0 - outerang);
            InnerAngle.Margin = margin;
            InnerAngle.Height = 400.0 - margin.Top - margin.Top + 14.0;
            InnerAngle.Width = InnerAngle.Height;
            InnerAngle.StrokeThickness = InnerAngle.Width / 2 * (1.0 - slider_innerang.Value);
        }
        
        // start live view by clicking 'stream from DE'
        public void btnLiveCapture_Click(object sender, RoutedEventArgs e)
        {
            
            if (_liveModeEnabled)
            {
                _liveModeEnabled = false;
                btnLiveCapture.Content = "Stream from DE";
                _updateTimer.Stop();
                //Dispatcher.InvokeShutdown();      // this just somehow works to stop streaming the image, software would go through BeginInvoke once then idle
            }
            else
            {
                
                //HDF5.InitializeHDF();   // initialize the HDF file used to save 3D data cube
                bool ImageRecon = false;
                if (EnableDetector.IsChecked == true) ImageRecon = true;
                ImageCount = 0;
                semaphore = new Semaphore(0, 1);
                new LiveModeView();
 //               Closing += LiveViewWindow_Closing;
                InitializeWBmp(GetImage()); // only used to display image on imagebox.1
                Show();
                _updateTimer = new System.Timers.Timer(10);
                _updateTimer.Elapsed += new ElapsedEventHandler(_updateTimer_Elapsed);

                InitializeWBmp(GetImage()); // initialize image in picture box
                                            //enable livemode on the server
                _deInterface.EnableLiveMode();
                _liveModeEnabled = true;
                btnLiveCapture.Content = "Stop Streaming";

                // start new task for background image rendering
                // determine size for each image

                string xSize = "";
                string ySize = "";
                _deInterface.GetProperty("Image Size X", ref xSize);
                _deInterface.GetProperty("Image Size Y", ref ySize);
                width = Convert.ToInt32(xSize);
                height = Convert.ToInt32(ySize);

                // determine how many frames to take
                string StrX = null;
                string StrY = null;
                int px = 0, py = 0;
                numpos = 0;

                PosX.Dispatcher.Invoke(
                    (ThreadStart)delegate { StrX = PosX.Text; }
                    );
                PosY.Dispatcher.Invoke(
                    (ThreadStart)delegate { StrY = PosX.Text; }
                    );

                if (Int32.TryParse(StrX, out px))
                {
                    if (Int32.TryParse(StrY, out py))
                    {
                        numpos = px * py;
                    }
                }

                //H5FileId fileId = HDF5.InitializeHDF(numpos, width, height);
                UInt16[,,] datacube = new UInt16[numpos,width,height];    // generate the data cube, each value should be an integer
                UInt16[] image = new UInt16[width*height];  // 1D image array used to save temp 2D frame
                // generate reconstruction bitmap and initialize _wBmpRecon
                UInt16[] recon = new UInt16[px * py]; // array for reconstrcution purpose
                UInt16[] recon_scale = new UInt16[px * py]; // array for scaled reconstrcuction image
                Bitmap ReconBMP = new Bitmap(px, py);   // bitmap for recon purpose
                BitmapSource ReconBitmapSource = ConvertBitmapSource(ReconBMP); // convert bitmap to bitmapsource, then can be used to generate writable bitmap
                InitializeWBmpRecon(ReconBitmapSource);

                int length = px * py;
                ushort min = recon[0];
                ushort max = recon[0];

                semaphore.Release();
                int nTickCount = 0;
                Task.Factory.StartNew(() =>
                {
                    while (_liveModeEnabled)
                    {
                        System.Threading.Thread.Sleep(1);
                        {
                            if (m_imageQueue.Count > 0)
                            // scale and display image
                            {
                                semaphore.WaitOne();
                                image = m_imageQueue.Dequeue();
                                semaphore.Release();
                                SetImage(image, width, height);
                                SetImageLoadTime(nTickCount);
                                // fill in array 'recon' for reconstruction image
                                double innerang = 0;
                                double outerang = 0;
                                slider_innerang.Dispatcher.Invoke(
                                    (ThreadStart)delegate { innerang = slider_innerang.Value; }
                                    );
                                slider_outerang.Dispatcher.Invoke(
                                    (ThreadStart)delegate { outerang = slider_outerang.Value; }
                                    );
                                
                                recon[ImageCount-1] = IntegrateBitmap(image, width, height, innerang, outerang);
                            }
                            if(ImageCount==1 && ImageRecon)   // case for first pixel
                            {
                                
                                min = recon[0];
                                max = recon[0];
                                recon_scale[0] = 255;  // rescale with new max and min
                                this.Dispatcher.BeginInvoke(new Action(() =>
                                {
                                    _wBmpRecon.WritePixels(new Int32Rect(0, 0, px, py), recon_scale, px * 2, 0);

                                }));

                                for (int x = 0; x < width; x++)
                                {
                                    for (int y = 0; y < height; y++)
                                    {
                                        datacube[0, x, y] = image[x * height + y];
                                    }
                                }
                            }

                            if(ImageCount > 1 && ImageRecon)
                            {
                                // imagecount would increase by 1 after setimage function, one more number on recon array
                                if (recon[ImageCount - 1] < min) min = recon[ImageCount - 1];
                                if (recon[ImageCount - 1] > max) max = recon[ImageCount - 1]; //update max and min after recon array changed
                                for (int i = 0; i < ImageCount; i++)
                                {
                                    recon_scale[i] = (ushort)((recon[i] - min) * 255 / (max - min + 1));  // rescale with new max and min
                                }
                                this.Dispatcher.BeginInvoke(new Action(() =>
                                {
                                    _wBmpRecon.WritePixels(new Int32Rect(0, 0, px, py), recon_scale, px * 2, 0);

                                }));
                                for (int x = 0; x < width; x++)
                                {
                                    for (int y = 0; y < height; y++)
                                    {
                                        datacube[ImageCount-1, x, y] = image[x*height+y];
                                    }
                                }
                            }
                            // criteria to stop image acquisition
                            if (ImageCount == numpos)
                            {
                                //HDF5.WriteDataCube(fileId, datacube);
                                _liveModeEnabled = false;
                                Dispatcher.BeginInvoke((Action)(() =>
                                {
                                    btnLiveCapture.Content = "Stream from DE";  //invoke is needed to control btn from another thread
                                }));
                                ImageCount = 0;
                                _updateTimer.Stop();
                                return;
                            }
                        }
                    }
                }).ContinueWith(p =>
                {

                });

                Task.Factory.StartNew(() =>
                {
                    while (_liveModeEnabled)
                    {
                        {
                            int nTickCountOld = 0;
                            //UInt16[] image;
                            nTickCountOld = System.Environment.TickCount;
                            _deInterface.GetImage(out image);   //get image from camera
                            nTickCount = System.Environment.TickCount - nTickCountOld; // get time elapsed
                            semaphore.WaitOne();
                            m_imageQueue.Enqueue(image);
                            semaphore.Release();
                        }
                        System.Threading.Thread.Sleep(1);
                    }
                }).ContinueWith(o =>
                {
                    if (_deInterface.isConnected())
                        _deInterface.DisableLiveMode();
                });
            }
        }

        public Bitmap CreateBitmap(ushort[] imagedata, int pxx, int pxy)
        {
            System.Drawing.Bitmap flag = new System.Drawing.Bitmap(pxx, pxy);
            for (int x = 0; x < pxx; x++)
            {
                for (int y = 0; y < pxy ; y++)
                {
                    int pixel = pxx * y + x;
                    flag.SetPixel(x, y, System.Drawing.Color.FromArgb(imagedata[pixel],imagedata[pixel],imagedata[pixel]));
                }
            }

            return flag;
        }


        // convert bitmap to BitmapSource
        public BitmapSource ConvertBitmapSource(System.Drawing.Bitmap bitmap)
        {
            var bitmapData = bitmap.LockBits(
                new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height),
                System.Drawing.Imaging.ImageLockMode.ReadOnly, bitmap.PixelFormat);

            var bitmapSource = BitmapSource.Create(
                bitmapData.Width, bitmapData.Height, 96, 96, PixelFormats.Gray8, null,
                bitmapData.Scan0, bitmapData.Stride * bitmapData.Height, bitmapData.Stride);

            bitmap.UnlockBits(bitmapData);
            return bitmapSource;
        }

        public void SetImage(UInt16[] imageData, int width, int height)
        {
            // Scale image
            int length = width * height;
            ushort min = imageData[0]; ushort max = imageData[0];
            for (int i = 1; i < length; i++)
            {
                if (imageData[i] < min) min = imageData[i];
                if (imageData[i] > max) max = imageData[i];
            }
            double gain = UInt16.MaxValue / Math.Max(max - min, 1);
            UInt16[] image16 = new UInt16[length];
            for (int i = 0; i < length; i++)
            {
                image16[i] = (ushort)((imageData[i] - min) * gain);
            }
            if (_firstImage)
            {
                _renderStart = DateTime.Now;
                _updateTimer.Start();
                _firstImage = false;
            }

            //use the dispatcher to invoke onto the UI thread for image displaying
            this.Dispatcher.BeginInvoke(new Action(() =>
            {
                //write the image data to the WriteableBitmap buffer
                _wBmp.WritePixels(new Int32Rect(0, 0, width, height), image16, width * 2, 0);

            }));
            ImageCount++;
            
        }

        // use 1D array as input, sum up intensity within range to return one single value
        public UInt16 IntegrateBitmap(UInt16[] imageData, int pxx, int pxy, double innerang, double outerang)
        {
            double centerx = pxx / 2;
            double centery = pxy / 2;
            if (pxx > pxy)
            {
                outerang = pxx * outerang;
            }
            else
            {
                outerang = pxy * outerang;
            }
                // use the smaller one among pxx and pxy to calculate outerang
            innerang = outerang * innerang;
            UInt16 sum=0;
            for (int i = 0; i<pxx; i++)
            {
                for (int j = 0; j < pxy;j++)
                {
                    double distance = Math.Pow(Convert.ToDouble(i - centerx), 2) + Math.Pow(Convert.ToDouble(j - centery), 2);
                    distance = Math.Sqrt(distance);
                    if (distance < outerang && distance > innerang)
                    {
                        sum += imageData[i+j*pxx];
                    }
                }
            }
            return sum;
        }

/*        private void LiveViewWindow_Closing(object sender, CancelEventArgs e)
        {
            _liveModeEnabled = false;
            Dispatcher.BeginInvoke((Action)(() =>
            {
                btnLiveCapture.Content = "Test Load Image Speed";
            }));
        }
*/

        // consider only get useful properties instead of getting all properties
        private void cmbCameras_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            CameraProperties.Clear();

            if (!_deInterface.isConnected())
                return;
            _deInterface.SetCameraName(cmbCameras.SelectedItem.ToString());

            List<String> props = new List<String>();
            _deInterface.GetPropertyNames(ref props);

            foreach (string propertyName in props)
            {
                string value = string.Empty;
                _deInterface.GetProperty(propertyName, ref value);
                CameraProperties.Add(new Property { Name = propertyName, Value = value });
            }
        }

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler PropertyChanged;
        private void NotifyPropertyChanged(String info)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(info));
            }
        }

        #endregion

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _deInterface = new DeInterfaceNET();
            //observable collection for Camera properties, used for binding with DataGrid
            CameraProperties = new ObservableCollection<Property>();
            NotifyPropertyChanged("CameraProperties");
        }

        private int _imageCount = 0;
        private bool _firstImage = true;
        private DateTime _renderStart;
        private System.Timers.Timer _updateTimer;
        private WriteableBitmap _wBmp;
        private WriteableBitmap _wBmpRecon;
        private int nTickCount = 0;
        private decimal dTickCountAvg = 0;
        private int nCount = 0;
        public decimal Fps
        {
           get { return Math.Round(Convert.ToDecimal(Convert.ToDouble(_imageCount) / TotalSeconds), 3); }
                        //get { return Convert.ToDecimal(TotalSeconds); }

        }

        public int ImageCount
        {
            get { return _imageCount; }
            set
            {
                _imageCount = value;
                NotifyPropertyChanged("ImageCount");
            }
        }

        public decimal Ilt
        {
            get
            {
                dTickCountAvg =
                     ((dTickCountAvg * nCount + nTickCount) / (nCount + 1));

                nCount++;
                return Math.Round((dTickCountAvg / 1000), 3);
            }
        }

        public double TotalSeconds
        {
            get
            {
                if (_firstImage == false)
                    return Math.Round(((DateTime.Now - _renderStart).TotalMilliseconds) / 1000);
                else
                    return 1;   // return 0 would cause N/0 error
            }
        }


        private void _updateTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (TotalSeconds > 1)   // add an extra criteria to avoid bugs when time is shorter than 1 second
            {
                NotifyPropertyChanged("Fps");
                NotifyPropertyChanged("TotalSeconds");
            }

        }

        /// <summary>
        /// Initialize the WriteableBitmap with a BitmapSource for the image specs
        /// </summary>
        /// <param name="bmpSource"></param>
        public void InitializeWBmp(BitmapSource bmpSource)
        {
            //_wBmp = new WriteableBitmap(bmpSource);

            _wBmp = new WriteableBitmap(bmpSource.PixelWidth, bmpSource.PixelHeight, bmpSource.DpiX, bmpSource.DpiY, bmpSource.Format, bmpSource.Palette);
            pictureBox1.Source = _wBmp; // display _wBmp to pictureBox1, will be called only once
        }

        public void InitializeWBmpRecon(BitmapSource bmpSource)
        {
            //_wBmp = new WriteableBitmap(bmpSource);

            _wBmpRecon = new WriteableBitmap(bmpSource.PixelWidth, bmpSource.PixelHeight, bmpSource.DpiX, bmpSource.DpiY, bmpSource.Format, bmpSource.Palette);
            Recon.Dispatcher.Invoke(
                (ThreadStart)delegate { Recon.Source = _wBmpRecon; }
            );
        }

        public void SetImageLoadTime(int nTickCount)
        {
            this.nTickCount = nTickCount;
            NotifyPropertyChanged("Ilt");
        }

        // change DE camera setting for number of pixels along x and y
        private void Submit_Setting_Click(object sender, RoutedEventArgs e)
        {
            string pxx = PixelsX.Text;
            string pxy = PixelsY.Text;
            _deInterface.SetProperty("Image Size X", pxx);
            _deInterface.SetProperty("Image Size Y", pxy);

        }

        private void EnableDetector_click(object sender, RoutedEventArgs e)
        {
                SolidColorBrush strokeBrush = new SolidColorBrush(Colors.Red);
                strokeBrush.Opacity = .25d;
                InnerAngle.Visibility = Visibility.Visible;
                InnerAngle.Stroke = strokeBrush;
                InnerAngle.Height = 400;
                InnerAngle.StrokeThickness = InnerAngle.Height / 2;
                slider_outerang.Value = 1;
                slider_innerang.Value = 0;
            ReadMRCfile();
        }

        private void DisableDetector_click(object sender, RoutedEventArgs e)
        {
            InnerAngle.Visibility = Visibility.Hidden;
        }

        private void EnableDetector_Checked(object sender, RoutedEventArgs e)
        {
        }
    }




        /*        #region INotifyPropertyChanged

                public event PropertyChangedEventHandler PropertyChanged;
                private void NotifyPropertyChanged(String info)
                {
                    if (PropertyChanged != null)
                    {
                        PropertyChanged(this, new PropertyChangedEventArgs(info));
                    }
                }

                #endregion
        */
    }

    public class Property
    {
        public string Name { get; set; }
        public string Value { get; set; }
    }

    enum ImageTransfer_Mode
    {
        ImageTransfer_Mode_TCP = 1,		// Use TCP/IP connected protocol (original mode)
        ImageTransfer_Mode_MMF = 2		// Use memory mapped file share buffer (local client only)
    };

    struct Mapped_Image_Data_
    {
        public bool client_opened_mmf;			// set to true by local client before connection to server
        public System.UInt32 buffer_size_;			// size of image buffer
        public System.UInt32 image_id_;		// image id, incremented with each new image transferred
        public System.UInt32 image_size_;		// image size in bytes
        public System.UInt32 img_start_;	// first pixel of image buffer
    };

    /// <summary>
    /// Interaction logic for LiveModeView.xaml
    /// </summary>
/*    public partial class LiveModeView : Window, INotifyPropertyChanged
    {
       
    }*/
    
