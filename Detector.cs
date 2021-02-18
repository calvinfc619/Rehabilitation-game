#if !(PLATFORM_LUMIN && !UNITY_EDITOR)

using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;
using OpenCVForUnity.CoreModule;
using OpenCVForUnity.ImgprocModule;
using OpenCVForUnity.UnityUtils;
using OpenCVForUnity.UnityUtils.Helper;

    [RequireComponent (typeof(WebCamTextureToMatHelper))]
    public class Detector : MonoBehaviour
    {
        private bool running = false;
        public void Enable(){
            running = true;
        }
        public void Disable(){
            running = false;
        }
        /// <summary>
        /// The texture.
        /// </summary>
        Texture2D texture;

        /// <summary>
        /// The webcam texture to mat helper.
        /// </summary>
        WebCamTextureToMatHelper webCamTextureToMatHelper;

        /// <summary>
        /// The gray mat.
        /// </summary>
        Mat grayMat;

        /// <summary>
        /// The FPS monitor.
        /// </summary>
        // FpsMonitor fpsMonitor;


        /// <summary>
        /// Ball tracker for logic
        /// </summary>
        BallTracker ball_tracker = new BallTracker();


        [SerializeField] RectTransform start_rect;
        [SerializeField] Canvas canvas;

        // Use this for initialization
        void Start ()
        {
            // fpsMonitor = GetComponent<FpsMonitor> ();

            webCamTextureToMatHelper = gameObject.GetComponent<WebCamTextureToMatHelper> ();

            #if UNITY_ANDROID && !UNITY_EDITOR
            // Avoids the front camera low light issue that occurs in only some Android devices (e.g. Google Pixel, Pixel2).
            webCamTextureToMatHelper.avoidAndroidFrontCameraLowLightIssue = true;
            #endif
            webCamTextureToMatHelper.Initialize ();


            //Calculates the start position based on the focus square
            if(start_rect != null){
                var corners = new Vector3[4];
                start_rect.GetWorldCorners(corners);
                var center = GetCenter(corners);
                var screen_point = Camera.main.WorldToScreenPoint(center);
                var world_point = Camera.main.ScreenToWorldPoint(center);

                Debug.Log($"WorldCenter: {center}, ScreenCenter: {screen_point}, World: {world_point}");
                ball_tracker.SetStartPosition(world_point);
            }else
                Enable();

            //Event bindings
            Events.ON_START_DETECTING.AddListener(this.Enable);
        }

        //Gets the center of the rectangle
        private Vector3 GetCenter(Vector3[] corners){
            Vector3 result = Vector3.zero;
            foreach (var corner in corners)
            {
                result += corner;
            }
            return result / corners.Length;
        }

        /// <summary>
        /// Raises the web cam texture to mat helper initialized event.
        /// </summary>

        public void OnWebCamTextureToMatHelperInitialized ()
        {
            Debug.Log ("OnWebCamTextureToMatHelperInitialized");

            Mat webCamTextureMat = webCamTextureToMatHelper.GetMat ();

            texture = new Texture2D (webCamTextureMat.cols (), webCamTextureMat.rows (), TextureFormat.RGBA32, false);
            Utils.matToTexture2D(webCamTextureMat, texture, webCamTextureToMatHelper.GetBufferColors());

            gameObject.GetComponent<Renderer> ().material.mainTexture = texture;

            gameObject.transform.localScale = new Vector3 ((float)Screen.width, (float)Screen.height, 1);

            Debug.Log ("Screen.width " + Screen.width + " Screen.height " + Screen.height + " Screen.orientation " + Screen.orientation);

            // if (fpsMonitor != null) {
            //     fpsMonitor.Add ("width", webCamTextureMat.width ().ToString ());
            //     fpsMonitor.Add ("height", webCamTextureMat.height ().ToString ());
            //     fpsMonitor.Add ("orientation", Screen.orientation.ToString ());
            // }

                                    
            float width = webCamTextureMat.width ();
            float height = webCamTextureMat.height ();
                                    
            float widthScale = (float)Screen.width / width;
            float heightScale = (float)Screen.height / height;
            if (widthScale < heightScale) {
                //Camera.main.orthographicSize = (width * (float)Screen.height / (float)Screen.width) / 2;
            } else {
                //Camera.main.orthographicSize = height / 2;
            }
            Camera.main.orthographicSize = Screen.height / 2;

            grayMat = new Mat (webCamTextureMat.rows (), webCamTextureMat.cols (), CvType.CV_8UC1);
        }

        /// <summary>
        /// Raises the web cam texture to mat helper disposed event.
        /// </summary>
        public void OnWebCamTextureToMatHelperDisposed ()
        {
            Debug.Log ("OnWebCamTextureToMatHelperDisposed");
            if (grayMat != null)
                grayMat.Dispose ();

            if (texture != null) {
                Texture2D.Destroy (texture);
                texture = null;
            }                        
        }

        /// <summary>
        /// Raises the web cam texture to mat helper error occurred event.
        /// </summary>
        /// <param name="errorCode">Error code.</param>
        public void OnWebCamTextureToMatHelperErrorOccurred (WebCamTextureToMatHelper.ErrorCode errorCode)
        {
            Debug.Log ("OnWebCamTextureToMatHelperErrorOccurred " + errorCode);
        }

        //Tweakable parameters for the tennis ball detection
        [SerializeField] public double dp = 2;
        [SerializeField] public double minDist = 10;
        [SerializeField] public double param1 = 160;
        [SerializeField] public double param2 = 50;
        [SerializeField] public int minRadius = 10;
        [SerializeField] public int maxRadius = 40;


        void Update ()
        {
            if(!running) return;


            if(Input.GetMouseButtonDown(0))
                Debug.Log($"Mouse Position: {Input.mousePosition} -> World: {Camera.main.ScreenToWorldPoint(Input.mousePosition)}");


            if (webCamTextureToMatHelper.IsPlaying () && webCamTextureToMatHelper.DidUpdateThisFrame ()) {

                Mat rgbaMat = webCamTextureToMatHelper.GetMat ();
                //Writes into the mat
                Imgproc.cvtColor (rgbaMat, grayMat, Imgproc.COLOR_RGB2GRAY);//COLOR_RGBA2GRAY
                //Applies gaussian blur for better results
                Imgproc.GaussianBlur(grayMat,grayMat,new Size(3,3),2);
                using (Mat circles = new Mat ()) {
                    
                    //Circle detection using the hough gradient
                    Imgproc.HoughCircles (grayMat, circles, Imgproc.CV_HOUGH_GRADIENT, dp,minDist,param1,param2,minRadius,maxRadius); 
                    Point pt = new Point ();

                    //Limits the circle drawing when too much circles are detected
                    if((int)circles.total() > 5){
                        for (int i = 0; i < circles.rows (); i++) {
                            double[] data = circles.get (i, 0);
                            pt.x = data [0];
                            pt.y = data [1];
                            double rho = data [2];
                            Imgproc.circle (rgbaMat, pt, (int)rho, GlobalValues.DETECTION_COLOR, GlobalValues.RINGS_RADIUS);
                        }
                    }else{//Tennis ball tracking starts here
                        for (int i = 0; i < circles.rows (); i++) {
                            for (var j = 0; j < circles.cols(); j++)
                            {
                                //Get the data from the API
                                double[] data = circles.get (i, j);
                                pt.x = data [0];
                                pt.y = data [1];
                                double rho = data [2];

                                //Convert to worldspace
                                Vector2 pos = new Vector2((float)data[0],webCamTextureToMatHelper.GetWebCamTexture().height -(float)data[1]);
                                Vector3 worldPos = Camera.main.ScreenToWorldPoint(AdjustToResolution(pos));

                                //Drawings for debug purposes
                                Debug.DrawRay(worldPos,Vector3.up*10,Color.magenta,1f);
                                Debug.DrawRay(worldPos,Vector3.down*10,Color.magenta,1f);
                                Debug.DrawRay(worldPos,Vector3.left*10,Color.magenta,1f);
                                Debug.DrawRay(worldPos,Vector3.right*10,Color.magenta,1f);

                                //If the ball went outside the detection threshold
                                if(ball_tracker.AwaitingForRegainFocus(worldPos)){
                                    //Flash a blue cirlcle to indicate the player where to start
                                    if(Mathf.Sin(Time.time * GlobalValues.CHECK_POINT_BLINKING_FRECUENCY) > 0){
                                        var last_pos = ball_tracker.GetLastPosition();
                                        var screen_pos = InvertAdjustToResolution(Camera.main.WorldToScreenPoint(last_pos));
                                        screen_pos.y = webCamTextureToMatHelper.GetWebCamTexture().height - screen_pos.y;
                                        Imgproc.circle (rgbaMat, new Point(screen_pos.x,screen_pos.y), (int)rho, GlobalValues.CHECK_POINT_COLOR, GlobalValues.RINGS_RADIUS);
                                    }
                                }//Otherwise Update the ball tracker
                                else if(ball_tracker.Update(worldPos))
                                    Imgproc.circle (rgbaMat, pt, (int)rho, GlobalValues.TRACKING_COLOR, GlobalValues.RINGS_RADIUS);
                            }
                        }
                    }

                }

//                Imgproc.putText (rgbaMat, "W:" + rgbaMat.width () + " H:" + rgbaMat.height () + " SO:" + Screen.orientation, new Point (5, rgbaMat.rows () - 10), Imgproc.FONT_HERSHEY_SIMPLEX, 1.0, new Scalar (255, 255, 255, 255), 2, Imgproc.LINE_AA, false);

                Utils.matToTexture2D (rgbaMat, texture, webCamTextureToMatHelper.GetBufferColors ());
            }
        }
        Vector3 AdjustToResolution(Vector3 pos){
            float x = ((float)Screen.width / webCamTextureToMatHelper.GetWebCamTexture().width);
            float y = ((float)Screen.height / webCamTextureToMatHelper.GetWebCamTexture().height);

            return new Vector3(pos.x*x,pos.y*y,1); 
        }
        Vector3 InvertAdjustToResolution(Vector3 pos){
            float x = ((float)Screen.width / webCamTextureToMatHelper.GetWebCamTexture().width);
            float y = ((float)Screen.height / webCamTextureToMatHelper.GetWebCamTexture().height);

            return new Vector3(pos.x/x,pos.y/y,1); 
        }
        /// <summary>
        /// Raises the destroy event.
        /// </summary>
        void OnDestroy ()
        {
            webCamTextureToMatHelper.Dispose ();
        }
        /// <summary>
        /// Raises the change camera button click event.
        /// </summary>
        public void OnChangeCameraButtonClick ()
        {
            webCamTextureToMatHelper.requestedIsFrontFacing = !webCamTextureToMatHelper.IsFrontFacing ();
        }
    }

#endif