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

    document.addEventListener("click", (event) => {
        const target = event.target;
        if (!(target instanceof Element)) return;
        const opener = target.closest("[data-dialog-open]");
        if (opener) {
            const dialog = document.getElementById(opener.getAttribute("data-dialog-open"));
            if (dialog instanceof HTMLDialogElement) {
                dialog.showModal();
                window.setTimeout(() => dialog.querySelector("input:not([type=hidden]), select, textarea, button")?.focus(), 0);
            }
            return;
        }
        if (target.closest("[data-dialog-close]")) target.closest("dialog")?.close();
    });

    document.querySelectorAll("dialog").forEach((dialog) => {
        dialog.addEventListener("click", (event) => {
            if (event.target === dialog) dialog.close();
        });
    });

    document.querySelectorAll("[data-confirm-form]").forEach((form) => {
        const input = form.querySelector("[data-confirm-input]");
        const submit = form.querySelector("[data-confirm-submit]");
        const expected = form.getAttribute("data-confirm-value") ?? "";
        if (!(input instanceof HTMLInputElement) || !(submit instanceof HTMLButtonElement)) return;
        input.addEventListener("input", () => {
            const value = input.value.trim();
            submit.disabled = value !== "DELETE" && value.toLocaleLowerCase() !== expected.toLocaleLowerCase();
        });
    });

    document.querySelectorAll("[data-tabs]").forEach((tabs) => {
        const buttons = [...tabs.querySelectorAll("[role=tab][data-tab]")];
        const select = (button, focus = false) => {
            buttons.forEach((item) => {
                const selected = item === button;
                item.setAttribute("aria-selected", String(selected));
                item.tabIndex = selected ? 0 : -1;
                const panel = document.getElementById(item.getAttribute("data-tab"));
                if (panel) panel.hidden = !selected;
            });
            if (focus) button.focus();
        };
        buttons.forEach((button, index) => {
            button.addEventListener("click", () => select(button));
            button.addEventListener("keydown", (event) => {
                if (!["ArrowLeft", "ArrowRight", "Home", "End"].includes(event.key)) return;
                event.preventDefault();
                const targetIndex = event.key === "Home" ? 0 :
                    event.key === "End" ? buttons.length - 1 :
                    event.key === "ArrowRight" ? (index + 1) % buttons.length :
                    (index - 1 + buttons.length) % buttons.length;
                select(buttons[targetIndex], true);
            });
        });
    });

    document.querySelectorAll("[data-document-reader]").forEach((reader) => {
        const selectors = [...reader.querySelectorAll("[data-document-select]")];
        const title = reader.querySelector("[data-document-title]");
        const frame = reader.querySelector("[data-document-frame]");
        const unavailable = reader.querySelector("[data-document-unavailable]");
        const download = reader.querySelector("[data-document-download]");
        selectors.forEach((selector) => selector.addEventListener("click", () => {
            selectors.forEach((item) => {
                const selected = item === selector;
                item.setAttribute("aria-pressed", String(selected));
                item.closest("li")?.classList.toggle("is-selected", selected);
            });
            const name = selector.getAttribute("data-document-name") ?? "Selected document";
            const previewUrl = selector.getAttribute("data-preview-url") ?? "";
            const downloadUrl = selector.getAttribute("data-download-url") ?? "";
            const message = selector.getAttribute("data-preview-message") || "Preview unavailable for this file type.";
            if (title) title.textContent = name;
            if (download instanceof HTMLAnchorElement) download.href = downloadUrl;
            if (frame instanceof HTMLIFrameElement) {
                frame.hidden = !previewUrl;
                frame.title = "Preview of " + name;
                if (previewUrl) frame.src = previewUrl;
                else frame.removeAttribute("src");
            }
            if (unavailable instanceof HTMLElement) {
                unavailable.hidden = Boolean(previewUrl);
                const paragraph = unavailable.querySelector("p");
                const fallbackDownload = unavailable.querySelector("a");
                if (paragraph) paragraph.textContent = message;
                if (fallbackDownload instanceof HTMLAnchorElement) fallbackDownload.href = downloadUrl;
            }
        }));
    });

    const allowedExtensions = new Set(["pdf", "doc", "docx", "xls", "xlsx", "png", "jpg", "jpeg", "txt"]);
    document.querySelectorAll("[data-file-upload]").forEach((workspace) => {
        const input = workspace.querySelector("[data-file-input]");
        const list = workspace.querySelector("[data-file-list]");
        const dropzone = workspace.querySelector(".upload-dropzone");
        if (!(input instanceof HTMLInputElement) || !(list instanceof HTMLElement)) return;
        let files = [];
        const apply = () => {
            const transfer = new DataTransfer();
            files.forEach((file) => transfer.items.add(file));
            input.files = transfer.files;
            list.hidden = files.length === 0;
            list.replaceChildren(...files.map((file, index) => {
                const extension = file.name.split(".").pop()?.toLocaleLowerCase() ?? "";
                const validType = allowedExtensions.has(extension);
                const validSize = file.size <= 25 * 1024 * 1024;
                const row = document.createElement("div");
                row.className = "upload-row" + (validType && validSize ? "" : " invalid");
                const info = document.createElement("div");
                const name = document.createElement("strong");
                name.textContent = file.name;
                const detail = document.createElement("small");
                detail.textContent = !validType ? "Unsupported file type" :
                    !validSize ? "File exceeds 25 MB" :
                    extension.toUpperCase() + " · " + formatBytes(file.size);
                info.append(name, detail);
                const remove = document.createElement("button");
                remove.type = "button";
                remove.className = "button small danger";
                remove.textContent = "Remove";
                remove.addEventListener("click", () => {
                    files.splice(index, 1);
                    apply();
                });
                row.append(info, remove);
                return row;
            }));
        };
        const addFiles = (incoming) => {
            const keys = new Set(files.map((file) => file.name + ":" + file.size + ":" + file.lastModified));
            [...incoming].forEach((file) => {
                const key = file.name + ":" + file.size + ":" + file.lastModified;
                if (!keys.has(key)) {
                    files.push(file);
                    keys.add(key);
                }
            });
            apply();
        };
        input.addEventListener("change", () => addFiles(input.files ?? []));
        dropzone?.addEventListener("dragover", (event) => {
            event.preventDefault();
            dropzone.classList.add("is-dragging");
        });
        dropzone?.addEventListener("dragleave", () => dropzone.classList.remove("is-dragging"));
        dropzone?.addEventListener("drop", (event) => {
            event.preventDefault();
            dropzone.classList.remove("is-dragging");
            addFiles(event.dataTransfer?.files ?? []);
        });
    });

    const formatBytes = (bytes) => {
        if (bytes >= 1024 * 1024) return (bytes / 1024 / 1024).toFixed(1) + " MB";
        return Math.max(0.1, bytes / 1024).toFixed(1) + " KB";
    };

    document.querySelectorAll("[data-wizard]").forEach((wizard) => {
        const steps = [...wizard.querySelectorAll("[data-wizard-step]")];
        const navigation = [...wizard.querySelectorAll("[data-wizard-nav]")];
        const previous = wizard.querySelector("[data-wizard-previous]");
        const next = wizard.querySelector("[data-wizard-next]");
        let current = 0;
        const show = (index) => {
            current = Math.max(0, Math.min(index, steps.length - 1));
            steps.forEach((step, stepIndex) => step.hidden = stepIndex !== current);
            navigation.forEach((item, stepIndex) => {
                item.classList.toggle("is-current", stepIndex === current);
                item.classList.toggle("is-complete", stepIndex < current);
                item.setAttribute("aria-current", stepIndex === current ? "step" : "false");
            });
            if (previous instanceof HTMLElement) previous.hidden = current === 0;
            if (next instanceof HTMLElement) next.hidden = current === steps.length - 1;
            if (current === steps.length - 1) updateReview(wizard);
            steps[current]?.querySelector("input, select, textarea, button")?.focus();
        };
        previous?.addEventListener("click", () => show(current - 1));
        next?.addEventListener("click", () => show(current + 1));
        navigation.forEach((item) => item.addEventListener("click", () => show(Number(item.getAttribute("data-wizard-nav")))));
        show(0);
    });

    const collectionTemplates = new WeakMap();
    document.querySelectorAll("[data-row-kind]").forEach((container) => {
        const source = container.querySelector("[data-collection-row]");
        if (source) collectionTemplates.set(container, source.cloneNode(true));
    });

    document.addEventListener("click", (event) => {
        const target = event.target;
        if (!(target instanceof Element)) return;
        const add = target.closest("[data-add-row]");
        if (add) {
            const container = document.getElementById(add.getAttribute("data-add-row"));
            const source = container ? collectionTemplates.get(container) : null;
            if (container && source) {
                const clone = source.cloneNode(true);
                clone.querySelectorAll("input, textarea").forEach((input) => {
                    if (input instanceof HTMLInputElement && input.type === "checkbox") {
                        input.checked = input.name.endsWith(".SendInApp");
                    }
                    else input.value = "";
                });
                clone.querySelectorAll("select").forEach((select) => select.selectedIndex = 0);
                container.append(clone);
                reindex(container);
                clone.querySelector("input, select, textarea")?.focus();
            }
            return;
        }
        const remove = target.closest("[data-remove-row]");
        if (remove) {
            const container = remove.closest("[data-row-kind]");
            remove.closest("[data-collection-row]")?.remove();
            if (container) reindex(container);
        }
    });

    const reindex = (container) => {
        const prefix = container.getAttribute("data-row-kind") === "field" ? "Fields" :
            container.getAttribute("data-row-kind") === "access" ? "Access" : "Notifications";
        [...container.querySelectorAll("[data-collection-row]")].forEach((row, index) => {
            row.querySelectorAll("[name]").forEach((control) => {
                control.name = control.name.replace(new RegExp(prefix + "\\[\\d+\\]"), prefix + "[" + index + "]");
            });
            row.querySelectorAll("[id]").forEach((control) => {
                control.id = control.id.replace(new RegExp(prefix + "_\\d+__"), prefix + "_" + index + "__");
            });
            row.querySelectorAll("label[for]").forEach((label) => {
                label.htmlFor = label.htmlFor.replace(new RegExp(prefix + "_\\d+__"), prefix + "_" + index + "__");
            });
        });
    };

    const updateReview = (wizard) => {
        const name = wizard.querySelector("[name=Name]")?.value?.trim();
        const prefix = wizard.querySelector("[name=NumberPrefix]")?.value?.trim();
        const target = wizard.querySelector("[data-review-name]");
        if (target) target.textContent = name ? name + (prefix ? " · " + prefix : "") : "Enter details in step 1";
        wizard.querySelectorAll("[data-review-count]").forEach((item) => {
            const container = document.getElementById(item.getAttribute("data-review-count"));
            item.textContent = (container?.querySelectorAll("[data-collection-row]").length ?? 0) + " configured";
        });
    };

    document.querySelectorAll("select[data-combobox]").forEach((select) => {
        const search = document.createElement("input");
        search.type = "search";
        search.className = "combobox-search";
        search.placeholder = "Search people";
        search.setAttribute("aria-label", "Search people");
        select.before(search);
        const options = [...select.options];
        search.addEventListener("input", () => {
            const term = search.value.trim().toLocaleLowerCase();
            options.forEach((option, index) => {
                if (index === 0) return;
                option.hidden = Boolean(term) && !option.text.toLocaleLowerCase().includes(term);
            });
        });
    });
})();
