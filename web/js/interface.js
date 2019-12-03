( function($) {
  'use strict';

    /*-------------------------------------------------------------------------------
	  Window load
	-------------------------------------------------------------------------------*/
	$(window).load(function(){

		$('.loader').fadeOut(200);
		$('body').addClass('body-loaded');

    	
	});

	var navbar=$('.js-navbar');
	var navbarAffixHeight=75

	/*-------------------------------------------------------------------------------
	  Smooth scroll to anchor
	-------------------------------------------------------------------------------*/

    $('.js-target-scroll').on('click', function() {
        var target = $(this.hash);
        if (target.length) {
            $('html,body').animate({
                scrollTop: (target.offset().top - navbarAffixHeight + 1)
            }, 1000);
            return false;
        }
    });
    
	/*-------------------------------------------------------------------------------
	 Scrollspy
	-------------------------------------------------------------------------------*/
	$('body').scrollspy({
		offset:  navbarAffixHeight + 1
	});
    $('.nav li').removeClass('active');
    $('.nav li:first').addClass('active');
})(jQuery);
