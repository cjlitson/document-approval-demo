(() => {
    const shell = document.querySelector(".app-shell");
    if (!shell) return;

    const sidebar = document.getElementById("app-sidebar");
    const collapseButton = shell.querySelector("[data-sidebar-toggle]");
    const mobileOpenButton = shell.querySelector("[data-sidebar-open]");
    const mobileCloseButton = shell.querySelector("[data-sidebar-close]");
    const desktopQuery = window.matchMedia("(min-width: 961px)");
    const storageKey = "document-routing.sidebar-collapsed";

    const setCollapsed = (collapsed) => {
        shell.classList.toggle("sidebar-collapsed", collapsed);
        if (collapseButton) {
            collapseButton.setAttribute("aria-expanded", String(!collapsed));
            collapseButton.setAttribute("title", collapsed ? "Expand navigation" : "Collapse navigation");
            const label = collapseButton.querySelector(".visually-hidden");
            if (label) label.textContent = collapsed ? "Expand navigation" : "Collapse navigation";
        }
    };

    const closeMobile = () => {
        shell.classList.remove("sidebar-mobile-open");
        mobileOpenButton?.setAttribute("aria-expanded", "false");
    };

    try {
        setCollapsed(desktopQuery.matches && localStorage.getItem(storageKey) === "true");
    } catch {
        setCollapsed(false);
    }

    collapseButton?.addEventListener("click", () => {
        const collapsed = !shell.classList.contains("sidebar-collapsed");
        setCollapsed(collapsed);
        try { localStorage.setItem(storageKey, String(collapsed)); } catch { }
    });

    mobileOpenButton?.addEventListener("click", () => {
        shell.classList.add("sidebar-mobile-open");
        mobileOpenButton.setAttribute("aria-expanded", "true");
        sidebar?.focus();
    });
    mobileCloseButton?.addEventListener("click", closeMobile);
    window.addEventListener("keydown", (event) => {
        if (event.key === "Escape") closeMobile();
    });
    desktopQuery.addEventListener("change", (event) => {
        if (event.matches) closeMobile();
    });

    document.addEventListener("click", (event) => {
        const target = event.target;
        if (!(target instanceof Element)) return;
        const row = target.closest(".clickable-row[data-href]");
        if (!row || target.closest("a, button, input, select, textarea, label, summary")) return;
        window.location.assign(row.getAttribute("data-href"));
    });

    document.addEventListener("keydown", (event) => {
        if (event.key !== "Enter" && event.key !== " ") return;
        const target = event.target;
        if (!(target instanceof HTMLElement) || !target.matches(".clickable-row[data-href]")) return;
        event.preventDefault();
        window.location.assign(target.getAttribute("data-href"));
    });

    document.addEventListener("click", (event) => {
        const target = event.target;
        if (!(target instanceof Element)) return;
        document.querySelectorAll("details.account-menu[open]").forEach((menu) => {
            if (!menu.contains(target)) menu.removeAttribute("open");
        });
    });
})();
