/*
   Deluxe Menu Data File
   Created by Deluxe Tuner v3.10
   http://deluxe-menu.com
*/



//--- Common
var menuIdentifier="cnet";
var isHorizontal=1;
var smColumns=3;
var smOrientation=0;
var dmRTL=0;
var pressedItem=-2;
var itemCursor="default";
var itemTarget="_self";
var statusString="link";
var blankImage="cnet.files/blank image filename";
var pathPrefix_img="";
var pathPrefix_link="";

//--- Dimensions
var menuWidth="";
var menuHeight="30px";
var smWidth="";
var smHeight="";

//--- Positioning
var absolutePos=0;
var posX="0px";
var posY="0px";
var topDX=0;
var topDY=0;
var DX=0;
var DY=0;
var subMenuAlign="left";
var subMenuVAlign="top";

//--- Font
var fontStyle=["normal 9pt arial,helvetica","normal 9pt arial,helvetica"];
var fontColor=["#3B6285","#3B6285"];
var fontDecoration=["none","underline"];
var fontColorDisabled="#AAAAAA";

//--- Appearance
var menuBackColor="transparent";
var menuBackImage="";
var menuBackRepeat="repeat";
var menuBorderColor="#999999";
var menuBorderWidth="0px";
var menuBorderStyle="solid";
var smFrameImage="cnet.files/sub.gif";
var smFrameWidth=10;

//--- Item Appearance
var itemBackColor=["#FFFFFF","#EBEBEB"];
var itemBackImage=["",""];
var itemSlideBack=0;
var beforeItemImage=["",""];
var afterItemImage=["",""];
var beforeItemImageW="";
var afterItemImageW="";
var beforeItemImageH="";
var afterItemImageH="";
var itemBorderWidth="0px 0px 1px 0px";
var itemBorderColor=["#E6E6E6","#E6E6E6"];
var itemBorderStyle=["solid","none"];
var itemSpacing=1;
var itemPadding="6px 5px";
var itemAlignTop="left";
var itemAlign="left";

//--- Icons
var iconTopWidth=24;
var iconTopHeight=24;
var iconWidth=16;
var iconHeight=16;
var arrowWidth=10;
var arrowHeight=10;
var arrowImageMain=["cnet.files/arrow.gif","cnet.files/arrow_o.gif"];
var arrowWidthSub=0;
var arrowHeightSub=0;
var arrowImageSub=["",""];

//--- Separators
var separatorImage="";
var separatorWidth="100%";
var separatorHeight="3px";
var separatorAlignment="left";
var separatorVImage="";
var separatorVWidth="1px";
var separatorVHeight="100%";
var separatorPadding="0px";

//--- Floatable Menu
var floatable=0;
var floatIterations=6;
var floatableX=1;
var floatableY=1;
var floatableDX=15;
var floatableDY=15;

//--- Movable Menu
var movable=0;
var moveWidth=12;
var moveHeight=20;
var moveColor="#AA0000";
var moveImage="";
var moveCursor="default";
var smMovable=0;
var closeBtnW=15;
var closeBtnH=15;
var closeBtn="";

//--- Transitional Effects & Filters
var transparency="100";
var transition=101;
var transOptions="";
var transDuration=300;
var transDuration2=200;
var shadowLen=0;
var shadowColor="#777777";
var shadowTop=1;

//--- CSS Support (CSS-based Menu)
var cssStyle=0;
var cssSubmenu="";
var cssItem=["",""];
var cssItemText=["",""];

//--- Advanced
var dmObjectsCheck=0;
var saveNavigationPath=1;
var showByClick=0;
var noWrap=1;
var smShowPause=200;
var smHidePause=1000;
var smSmartScroll=1;
var topSmartScroll=0;
var smHideOnClick=1;
var dm_writeAll=0;
var useIFRAME=0;
var dmSearch=0;

//--- AJAX-like Technology
var dmAJAX=0;
var dmAJAXCount=0;
var ajaxReload=0;

//--- Dynamic Menu
var dynamic=0;

//--- Popup Menu
var popupMode=0;

//--- Keystrokes Support
var keystrokes=0;
var dm_focus=1;
var dm_actKey=113;

//--- Sound
var onOverSnd="";
var onClickSnd="";

var itemStyles = [
    ["itemHeight=26px","itemBackColor=#DEDEDE,#1E5B7E","itemBackImage=cnet.files/top.png,cnet.files/top.png","itemSlideBack=5","fontStyle='bold 12px arial,helvetica','bold 12px arial,helvetica'","fontColor=#484848,#FFFFFF","fontDecoration=none,none"],
    ["itemHeight=26px","itemBackColor=transparent,transparent","itemBackImage=cnet.files/sub-item-normal.gif,cnet.files/sub-item-over.gif"],
];
var menuStyles = [
    ["smColumns=2"],
    ["itemSpacing=0","itemPadding=5px 10px"],
];
dm_init();