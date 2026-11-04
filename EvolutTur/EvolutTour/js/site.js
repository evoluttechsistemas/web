(function () {
    'use strict';

    angular
        .module('app')
        .controller('site', site);

    site.$inject = ['$location'];

    function site($location) {
        /* jshint validthis:true */
        var vm = this;
        vm.title = 'site';

        activate();

        function activate() { }
    }
})();

function initTinyMCE() {
    tinymce.init({
        selector: '#editor',
        height: 300
    });
}

function getTinyMCEContent() {
    return tinymce.get('editor').getContent();
}
