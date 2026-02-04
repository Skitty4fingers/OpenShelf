(function () {
    const html = document.documentElement;
    const themeParams = {
        LIGHT: 'light',
        DARK: 'dark',
        STORAGE_KEY: 'openshelf-theme'
    };

    function getPreferredTheme() {
        const storedTheme = localStorage.getItem(themeParams.STORAGE_KEY);
        if (storedTheme) {
            return storedTheme;
        }
        return window.matchMedia('(prefers-color-scheme: dark)').matches ? themeParams.DARK : themeParams.LIGHT;
    }

    function setTheme(theme) {
        if (theme === themeParams.DARK) {
            html.setAttribute('data-theme', 'dark');
            html.setAttribute('data-bs-theme', 'dark');
        } else {
            html.setAttribute('data-theme', 'light');
            html.setAttribute('data-bs-theme', 'light');
        }
        localStorage.setItem(themeParams.STORAGE_KEY, theme);
        updateIcon(theme);
    }

    function updateIcon(theme) {
        const icon = document.getElementById('theme-icon');
        if (icon) {
            if (theme === themeParams.DARK) {
                icon.classList.remove('bi-moon-stars');
                icon.classList.add('bi-sun');
            } else {
                icon.classList.remove('bi-sun');
                icon.classList.add('bi-moon-stars');
            }
        }
    }

    function toggleTheme() {
        const currentTheme = html.getAttribute('data-theme') || getPreferredTheme();
        const newTheme = currentTheme === themeParams.DARK ? themeParams.LIGHT : themeParams.DARK;
        setTheme(newTheme);
    }

    // Initialize
    const preferredTheme = getPreferredTheme();
    setTheme(preferredTheme); // Set immediately to avoid flash

    // Expose toggle function globally
    window.toggleTheme = toggleTheme;

    // Wait for DOM to update icon
    document.addEventListener('DOMContentLoaded', () => {
        updateIcon(preferredTheme);
    });
})();
