// JavaScript
window.addEventListener('load', function() {
        var interval = setInterval(function() {
            var topbar = document.querySelector('#swagger-ui .topbar .wrapper .topbar-wrapper');

            if (topbar) {
                clearInterval(interval); // Found topbar, stop searching

                var btn = document.createElement('button');
                btn.innerText = 'Logout';
                btn.style.marginLeft = '20px';
                btn.onclick = function () {
                    // Simple navigation - no CORS issues
                    window.location.href = '/frontend/environment/logout?returnUrl=/swagger/frontend';
                };
                topbar.appendChild(btn);
            }
        }, 500);
});
