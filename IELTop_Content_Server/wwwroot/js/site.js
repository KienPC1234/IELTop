// IELTop Content Server client script

// Timezone and language detection across all pages
(function () {
    try {
        var tz = '';
        if (typeof Intl !== 'undefined' && Intl.DateTimeFormat) {
            tz = Intl.DateTimeFormat().resolvedOptions().timeZone || '';
        }

        // Store detected client timezone in cookie so server can read it
        if (tz) {
            var currentTzMatch = document.cookie.match(/(?:^|; )ieltop_tz=([^;]*)/);
            var currentTz = currentTzMatch ? decodeURIComponent(currentTzMatch[1]) : '';
            if (currentTz !== tz) {
                document.cookie = 'ieltop_tz=' + encodeURIComponent(tz) + '; path=/; max-age=31536000; SameSite=Lax';
            }
        }

        // Heuristic detection: Vietnam / Indochina timezones or Vietnamese browser language
        var isVnTz = tz === 'Asia/Ho_Chi_Minh' || tz === 'Asia/Saigon' || tz === 'Asia/Bangkok' || tz === 'Asia/Hanoi';
        var offset = new Date().getTimezoneOffset(); // -420 for UTC+7 (Vietnam)
        if (offset === -420) {
            isVnTz = true;
        }

        var browserLang = (navigator.language || navigator.userLanguage || '').toLowerCase();
        var isVnLang = browserLang.indexOf('vi') === 0;

        var currentLangMatch = document.cookie.match(/(?:^|; )ieltop_lang=([^;]*)/);
        if (!currentLangMatch && (isVnTz || isVnLang)) {
            // Initialize default language as 'vi' for users in Vietnam
            document.cookie = 'ieltop_lang=vi; path=/; max-age=31536000; SameSite=Lax';
        }
    } catch (e) {
        // Silently pass in legacy or restricted environments
    }
})();

// Global language switcher helper
window.setIeltopLang = function (lang) {
    if (lang === 'vi' || lang === 'en') {
        document.cookie = 'ieltop_lang=' + encodeURIComponent(lang) + '; path=/; max-age=31536000; SameSite=Lax';
        var url = new URL(window.location.href);
        url.searchParams.set('lang', lang);
        window.location.href = url.toString();
    }
};

document.addEventListener('DOMContentLoaded', function () {
    const toggleBtn = document.getElementById('themeToggleBtn');
    const iconSun = document.getElementById('themeIconSun');
    const iconMoon = document.getElementById('themeIconMoon');
    const themeText = document.getElementById('themeText');

    function updateIcons(theme) {
        if (!iconSun || !iconMoon) return;
        if (theme === 'dark') {
            iconSun.classList.remove('d-none');
            iconMoon.classList.add('d-none');
            if (themeText) themeText.textContent = 'Light';
        } else {
            iconSun.classList.add('d-none');
            iconMoon.classList.remove('d-none');
            if (themeText) themeText.textContent = 'Dark';
        }
    }

    const currentTheme = document.documentElement.getAttribute('data-bs-theme') || 'light';
    updateIcons(currentTheme);

    if (toggleBtn) {
        toggleBtn.addEventListener('click', function () {
            const activeTheme = document.documentElement.getAttribute('data-bs-theme');
            const newTheme = activeTheme === 'dark' ? 'light' : 'dark';
            document.documentElement.setAttribute('data-bs-theme', newTheme);
            localStorage.setItem('ieltop_theme', newTheme);
            updateIcons(newTheme);
        });
    }
});
