//---- Menu -----//

function myFunction(x) {
    x.classList.toggle("change");
}

$(document).ready(function() {
    $(".icon_menu").click(function(){
		$(".menu").slideToggle("slow");
	});
});


//--- Button Top---//


$(window).scroll(function(){
    if ($(this).scrollTop() > 100) {
        $('#btn-scrollup').fadeIn();
    } else {
        $('#btn-scrollup').fadeOut();
    }
});
$('#btn-scrollup').click(function(){
    $("html, body").animate({ scrollTop: 0 }, 600);
    return false;
});

//--Slider--//

$(document).ready(function(){	
		var slider = new MasterSlider();
		slider.setup('masterslider' , {
			width:'1170',
			height:'auto',
			space:0,
			view:'slide',
            fullwidth:true,
			autofill:true,
			speed:20,
            autoplay:false
		});
		
		slider.control('arrows' ,{insertTo:'#masterslider'});	
		slider.control('bullets');


var wrapper = $('#slider1-wrapper');
		wrapper.height(window.innerHeight + 5);
		$(window).resize(function(event) {
			wrapper.height(window.innerHeight - 5);
		});	
    
});

//--Contactus validation--//

$(document).ready(function() {    
        $("#mailForm1").validate({
      rules: {
                name: {
                    required:true
                },
                email: {
                    required: true,
                    email: true
                  },
                mobile: {
                    required: true,
                    number:true,
                    maxlength:12,
                    minlength:10
                },
                resume:{
                    required: true
                },
                pos_career: {
                    required: true
                },
				message:{
					required: true
				},
               },   
      messages: {

                name: {
                    required:"Please enter your name"
                },

                email: {
                    required:"Please enter your email ID "
                },
          
                mobile: {
                     required:"Please enter your mobile number"
                }, 
                resume: {
                    required:"Please upload your resume"
                },
                pos_career: {
                     required:"Please enter your position"
                },  
				message: {
                     required:"Please enter your Description"
                },
            },
            submitHandler: function() {
                $.ajax({
                    type:'post',
                    url: "mail.php",
                    data: $("#mailForm").serialize(),
                    success: function(result){                         
                    $.magnificPopup.open({
                      items: {
                        src: '<div id="success-massage" class="zoom-anim-dialog small-dialog success forgot_width"><h3>Thank you for contacting us. We will get back you soon.</h3></div>',
                        type: 'inline'
                      }
                    });
                    $("#mailForm")[0].reset();


                    }

                });
            }
        }); 
    });	


$(document).ready(function() {    
        $("#mailForm2").validate({
      rules: {
                fullname: {
                    required:true
                },
                eemail: {
                    required: true,
                    email: true
                  },
                mobile_c:{
					required: true
				},
                position:{
					required: true
				},
                address:{
					required: true
				},
				message:{
					required: true
				},
               },   
      messages: {

                fullname: {
                    required:"Please enter your full name"
                },

                eemail: {
                    required:"Please enter your email ID "
                },
          
                mobile_c: {
                    required:"Please enter your mobile number "
                },
          
                position: {
                    required:"Please enter your position "
                },
          
                address: {
                    required:"Please enter your address "
                },
          
				message: {
                     required:"Please enter your description"
                },
            },
            submitHandler: function() {
                $.ajax({
                    type:'post',
                    url: "mail.php",
                    data: $("#mailForm").serialize(),
                    success: function(result){                         
                    $.magnificPopup.open({
                      items: {
                        src: '<div id="success-massage" class="zoom-anim-dialog small-dialog success forgot_width"><h3>Thank you for contacting us. We will get back you soon.</h3></div>',
                        type: 'inline'
                      }
                    });
                    $("#mailForm")[0].reset();


                    }

                });
            }
        }); 
    });	


//--popup--//

$(document).ready(function() {
	
		$('.popup-with-move-anim').magnificPopup({
		type: 'inline',

		fixedContentPos: false,
		fixedBgPos: true,

		overflowY: 'auto',

		closeBtnInside: true,
		preloader: false,
		
		midClick: true,
		removalDelay: 300,
		mainClass: 'my-mfp-slide-bottom'
	});

	 });


 $('.resume').bind("click" , function () {
        $('#file-type').click();
    });



$('a.goNext').click(function() {
$('html,body').animate({ scrollTop: $(this.hash).offset().top-65}, 1000);
return false;

e.preventDefault();

});