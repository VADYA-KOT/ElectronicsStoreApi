const API_BASE_URL = "/api";
const PRODUCTS_URL = `${API_BASE_URL}/products`;
const CATEGORIES_URL = `${API_BASE_URL}/categories`;
const ACCESS_TOKEN_KEY = "electronicsStoreAccessToken";
const USERNAME_KEY = "electronicsStoreUsername";
const MAX_IMAGE_SIZE = 5 * 1024 * 1024;

const state = {
    products: [],
    categories: [],
    editingProduct: null,
    selectedImage: null,
    currentImageUrl: null,
    previewObjectUrl: null,
    isBusy: false
};

const elements = {
    accessCard: document.querySelector("#admin-access-card"),
    accessMessage: document.querySelector("#admin-access-message"),
    app: document.querySelector("#admin-app"),
    message: document.querySelector("#admin-message"),
    logoutButton: document.querySelector("#admin-logout-button"),
    productForm: document.querySelector("#product-form"),
    productFormTitle: document.querySelector("#product-form-title"),
    productName: document.querySelector("#product-name"),
    productPrice: document.querySelector("#product-price"),
    productOldPrice: document.querySelector("#product-old-price"),
    productQuantity: document.querySelector("#product-quantity"),
    productCategory: document.querySelector("#product-category"),
    productDescription: document.querySelector("#product-description"),
    productImage: document.querySelector("#product-image"),
    imagePreview: document.querySelector("#image-preview"),
    removeImageButton: document.querySelector("#remove-product-image"),
    saveProductButton: document.querySelector("#save-product-button"),
    cancelEditButton: document.querySelector("#cancel-edit-button"),
    productsBody: document.querySelector("#admin-products-body"),
    productSearch: document.querySelector("#admin-product-search"),
    refreshProductsButton: document.querySelector("#refresh-products-button"),
    categoryForm: document.querySelector("#category-form"),
    categoryName: document.querySelector("#category-name"),
    categoryList: document.querySelector("#admin-category-list"),
    statProducts: document.querySelector("#stat-products"),
    statInStock: document.querySelector("#stat-in-stock"),
    statDiscounts: document.querySelector("#stat-discounts")
};

class ApiError extends Error {
    constructor(message, status) {
        super(message);
        this.name = "ApiError";
        this.status = status;
    }
}

function readAccessToken() {
    return sessionStorage.getItem(ACCESS_TOKEN_KEY);
}

function decodeJwtPayload(token) {
    try {
        const payloadPart = token.split(".")[1];
        if (!payloadPart) return null;

        const base64 = payloadPart
            .replace(/-/g, "+")
            .replace(/_/g, "/")
            .padEnd(Math.ceil(payloadPart.length / 4) * 4, "=");

        return JSON.parse(atob(base64));
    } catch (error) {
        console.error("Не удалось прочитать JWT:", error);
        return null;
    }
}

function tokenHasAdminRole(token) {
    const payload = decodeJwtPayload(token);
    if (!payload) return false;

    const roleClaimNames = [
        "role",
        "roles",
        "http://schemas.microsoft.com/ws/2008/06/identity/claims/role"
    ];

    const roles = roleClaimNames.flatMap(key => {
        const value = payload[key];
        if (Array.isArray(value)) return value;
        return value ? [value] : [];
    });

    return roles.some(role => String(role).toLowerCase() === "admin");
}

function showAccessRequired(message) {
    elements.app.hidden = true;
    elements.accessCard.hidden = false;
    elements.accessMessage.textContent = message;
}

function showMessage(message, type = "") {
    elements.message.textContent = message;
    elements.message.className = "admin-notice";

    if (type === "success") {
        elements.message.classList.add("admin-notice--success");
    } else if (type === "error") {
        elements.message.classList.add("admin-notice--error");
    }
}

function getErrorMessage(data, fallback) {
    if (!data) return fallback;
    if (typeof data === "string" && data.trim()) return data;
    if (typeof data.message === "string") return data.message;
    if (typeof data.title === "string") return data.title;
    if (typeof data.detail === "string") return data.detail;

    if (data.errors && typeof data.errors === "object") {
        const firstError = Object.values(data.errors).flat().find(Boolean);
        if (firstError) return String(firstError);
    }

    return fallback;
}

async function apiRequest(url, options = {}) {
    const token = readAccessToken();
    const headers = new Headers(options.headers ?? {});

    if (token) {
        headers.set("Authorization", `Bearer ${token}`);
    }

    // Для FormData браузер сам устанавливает multipart boundary.
    const response = await fetch(url, {
        ...options,
        headers
    });

    const responseText = await response.text();
    let data = null;

    if (responseText) {
        try {
            data = JSON.parse(responseText);
        } catch {
            data = responseText;
        }
    }

    if (response.status === 401) {
        sessionStorage.removeItem(ACCESS_TOKEN_KEY);
        sessionStorage.removeItem(USERNAME_KEY);

        showAccessRequired(
            "Сессия завершена или токен недействителен. Войдите в аккаунт администратора заново."
        );

        throw new ApiError("Требуется повторная авторизация.", 401);
    }

    if (!response.ok) {
        const fallback = `Ошибка запроса: HTTP ${response.status}`;
        throw new ApiError(getErrorMessage(data, fallback), response.status);
    }

    return { response, data };
}

function extractItems(data) {
    if (Array.isArray(data)) return data;
    if (Array.isArray(data?.items)) return data.items;
    if (Array.isArray(data?.Items)) return data.Items;
    return [];
}

function extractProductId(data) {
    const id = data?.id ?? data?.Id ?? data?.product?.id ?? data?.product?.Id;
    const number = Number(id);

    return Number.isInteger(number) && number > 0 ? number : null;
}

function extractCategoryId(category) {
    const id = Number(category?.id ?? category?.Id);

    return Number.isInteger(id) && id >= 0 ? id : null;
}

function formatPrice(value) {
    const number = Number(value);

    return Number.isFinite(number)
        ? `${number.toFixed(2)} BYN`
        : "—";
}

function getDiscountPercent(product) {
    const oldPrice = Number(product.oldPrice);
    const price = Number(product.price);

    if (!Number.isFinite(oldPrice) || !Number.isFinite(price) || oldPrice <= price || oldPrice <= 0) {
        return null;
    }

    return Math.round((1 - price / oldPrice) * 100);
}

// ==============================
// Загрузка данных
// ==============================

async function loadProducts() {
    elements.productsBody.innerHTML =
        '<tr><td class="admin-table-empty" colspan="6">Загрузка товаров...</td></tr>';

    const pageSize = 100;

    const first = await apiRequest(
        `${PRODUCTS_URL}?page=1&pageSize=${pageSize}`
    );

    let products = extractItems(first.data);

    const totalPages = Number(
        first.data?.totalPages ?? first.data?.TotalPages ?? 1
    );

    if (Number.isInteger(totalPages) && totalPages > 1 && totalPages <= 100) {
        const pageRequests = [];

        for (let page = 2; page <= totalPages; page++) {
            pageRequests.push(
                apiRequest(`${PRODUCTS_URL}?page=${page}&pageSize=${pageSize}`)
            );
        }

        const pages = await Promise.all(pageRequests);

        for (const pageResult of pages) {
            products = products.concat(extractItems(pageResult.data));
        }
    }

    state.products = products;

    renderProducts();
    renderStats();
}

async function loadCategories() {
    const result = await apiRequest(CATEGORIES_URL);

    state.categories = extractItems(result.data);

    renderCategoryOptions();
    renderCategoryList();
}

// ==============================
// Категории
// ==============================

function renderCategoryOptions(selectedId = elements.productCategory.value) {
    const currentValue = String(selectedId ?? "");

    elements.productCategory.replaceChildren();

    const emptyOption = document.createElement("option");
    emptyOption.value = "";
    emptyOption.textContent = "Без категории";

    elements.productCategory.append(emptyOption);

    for (const category of state.categories) {
        const option = document.createElement("option");

        option.value = String(category.id ?? category.Id);
        option.textContent = category.name ?? category.Name ?? "Категория";

        elements.productCategory.append(option);
    }

    elements.productCategory.value = currentValue;
}

function renderCategoryList() {
    elements.categoryList.replaceChildren();

    if (state.categories.length === 0) {
        const message = document.createElement("span");

        message.className = "admin-category-chip";
        message.textContent = "Категорий пока нет";

        elements.categoryList.append(message);
        return;
    }

    for (const category of state.categories) {
        const id = extractCategoryId(category);

        const name = category.name ?? category.Name ?? "Категория";

        const productCount = Number(
            category.productCount ?? category.ProductCount ?? 0
        );

        const chip = document.createElement("div");
        chip.className = "admin-category-chip";

        const label = document.createElement("span");
        label.textContent = `${name} · товаров: ${productCount}`;

        const actions = document.createElement("div");
        actions.className = "admin-row-actions";

        const editButton = createActionButton(
            "Изменить",
            "admin-button admin-button--secondary admin-button--small",
            () => renameCategory(category)
        );

        const deleteButton = createActionButton(
            "Удалить",
            "admin-button admin-button--danger admin-button--small",
            () => deleteCategory(category)
        );

        editButton.disabled = state.isBusy || id === null;
        deleteButton.disabled = state.isBusy || id === null;

        actions.append(editButton, deleteButton);
        chip.append(label, actions);

        elements.categoryList.append(chip);
    }
}

// ==============================
// Статистика
// ==============================

function renderStats() {
    const total = state.products.length;

    const inStock = state.products.filter(
        product => Number(product.quantity) > 0
    ).length;

    const discounted = state.products.filter(
        product => getDiscountPercent(product) !== null
    ).length;

    elements.statProducts.textContent = String(total);
    elements.statInStock.textContent = String(inStock);
    elements.statDiscounts.textContent = String(discounted);
}

// ==============================
// Вспомогательные элементы
// ==============================

function createImageCell(imageUrl) {
    const wrapper = document.createElement("div");
    wrapper.className = "admin-table__image";

    if (imageUrl) {
        const image = document.createElement("img");

        image.src = imageUrl;
        image.alt = "Фотография товара";
        image.loading = "lazy";

        image.onerror = () => {
            wrapper.textContent = "Нет фото";
        };

        wrapper.append(image);
    } else {
        wrapper.textContent = "Нет фото";
    }

    return wrapper;
}

function createActionButton(text, className, handler) {
    const button = document.createElement("button");

    button.type = "button";
    button.className = className;
    button.textContent = text;

    button.addEventListener("click", handler);

    return button;
}

// ==============================
// Товары
// ==============================

function renderProducts() {
    const query = elements.productSearch.value
        .trim()
        .toLocaleLowerCase("ru-RU");

    const filteredProducts = state.products.filter(product => {
        const name = String(product.name ?? "")
            .toLocaleLowerCase("ru-RU");

        const category = String(product.categoryName ?? "")
            .toLocaleLowerCase("ru-RU");

        return !query || name.includes(query) || category.includes(query);
    });

    elements.productsBody.replaceChildren();

    if (filteredProducts.length === 0) {
        const row = document.createElement("tr");
        const cell = document.createElement("td");

        cell.colSpan = 6;
        cell.className = "admin-table-empty";

        cell.textContent = state.products.length === 0
            ? "В каталоге пока нет товаров. Добавьте первый товар выше."
            : "По вашему запросу товары не найдены.";

        row.append(cell);
        elements.productsBody.append(row);

        return;
    }

    const fragment = document.createDocumentFragment();

    for (const product of filteredProducts) {
        const row = document.createElement("tr");

        const imageCell = document.createElement("td");
        imageCell.append(createImageCell(product.imageUrl));

        const nameCell = document.createElement("td");
        nameCell.className = "admin-product-name";
        nameCell.textContent = product.name ?? "Без названия";

        const categoryCell = document.createElement("td");
        categoryCell.textContent = product.categoryName ?? "Без категории";

        const priceCell = document.createElement("td");

        const currentPrice = document.createElement("span");
        currentPrice.className = "admin-price-current";
        currentPrice.textContent = formatPrice(product.price);

        priceCell.append(currentPrice);

        const discount = getDiscountPercent(product);

        if (discount !== null) {
            const oldPrice = document.createElement("span");
            oldPrice.className = "admin-price-old";
            oldPrice.textContent = formatPrice(product.oldPrice);

            const label = document.createElement("span");
            label.className = "admin-sale-label";
            label.textContent = `−${discount}%`;

            priceCell.append(oldPrice, label);
        }

        const stockCell = document.createElement("td");

        const quantity = Number(product.quantity);

        stockCell.className = quantity > 0
            ? "admin-stock--available"
            : "admin-stock--empty";

        stockCell.textContent = Number.isFinite(quantity)
            ? `${quantity} шт.`
            : "—";

        const actionsCell = document.createElement("td");

        const actions = document.createElement("div");
        actions.className = "admin-row-actions";

        actions.append(
            createActionButton(
                "Изменить",
                "admin-button admin-button--secondary admin-button--small",
                () => startEditing(product)
            ),
            createActionButton(
                "Удалить",
                "admin-button admin-button--danger admin-button--small",
                () => deleteProduct(product)
            )
        );

        actionsCell.append(actions);

        row.append(
            imageCell,
            nameCell,
            categoryCell,
            priceCell,
            stockCell,
            actionsCell
        );

        fragment.append(row);
    }

    elements.productsBody.append(fragment);
}

// ==============================
// Работа с фотографиями
// ==============================

function clearPreviewObjectUrl() {
    if (state.previewObjectUrl) {
        URL.revokeObjectURL(state.previewObjectUrl);
        state.previewObjectUrl = null;
    }
}

function setImagePreview(url) {
    clearPreviewObjectUrl();

    elements.imagePreview.replaceChildren();

    if (!url) {
        elements.imagePreview.textContent = "Предпросмотр изображения";
        return;
    }

    const image = document.createElement("img");

    image.src = url;
    image.alt = "Предпросмотр фотографии товара";

    image.onerror = () => {
        elements.imagePreview.textContent = "Не удалось загрузить фото";
    };

    elements.imagePreview.append(image);
}

// ==============================
// Форма товара
// ==============================

function resetProductForm() {
    state.editingProduct = null;
    state.selectedImage = null;
    state.currentImageUrl = null;

    elements.productForm.reset();

    elements.productQuantity.value = "0";

    elements.productFormTitle.textContent = "Добавить товар";
    elements.saveProductButton.textContent = "Сохранить товар";

    elements.cancelEditButton.hidden = true;
    elements.removeImageButton.hidden = true;

    elements.productImage.value = "";

    setImagePreview(null);
    renderCategoryOptions("");
}

function startEditing(product) {
    state.editingProduct = product;
    state.selectedImage = null;
    state.currentImageUrl = product.imageUrl ?? null;

    elements.productFormTitle.textContent =
        `Редактирование товара №${product.id}`;

    elements.productName.value = product.name ?? "";
    elements.productPrice.value = product.price ?? "";
    elements.productOldPrice.value = product.oldPrice ?? "";
    elements.productQuantity.value = product.quantity ?? 0;
    elements.productDescription.value = product.description ?? "";

    renderCategoryOptions(product.categoryId ?? "");

    elements.productImage.value = "";

    setImagePreview(state.currentImageUrl);

    elements.removeImageButton.hidden = !state.currentImageUrl;

    elements.saveProductButton.textContent = "Сохранить изменения";
    elements.cancelEditButton.hidden = false;

    document.querySelector("#product-editor-section").scrollIntoView({
        behavior: "smooth",
        block: "start"
    });
}

function validateProductForm() {
    const name = elements.productName.value.trim();

    const price = Number(elements.productPrice.value);

    const oldPriceInput = elements.productOldPrice.value.trim();

    const oldPrice = oldPriceInput === ""
        ? null
        : Number(oldPriceInput);

    const quantity = Number(elements.productQuantity.value);

    if (!name) {
        throw new Error("Укажите название товара.");
    }

    if (!Number.isFinite(price) || price <= 0) {
        throw new Error("Текущая цена должна быть больше нуля.");
    }

    if (oldPrice !== null && (!Number.isFinite(oldPrice) || oldPrice <= price)) {
        throw new Error("Старая цена должна быть выше текущей цены.");
    }

    if (!Number.isInteger(quantity) || quantity < 0) {
        throw new Error("Количество должно быть целым числом не меньше нуля.");
    }

    const categoryIdValue = elements.productCategory.value;

    const categoryId = categoryIdValue === ""
        ? null
        : Number(categoryIdValue);

    return {
        name,
        price,
        oldPrice,
        quantity,
        categoryId,
        description: elements.productDescription.value.trim() || null
    };
}

async function uploadProductImage(productId, file) {
    const formData = new FormData();

    formData.append("file", file);

    await apiRequest(`${PRODUCTS_URL}/${productId}/image`, {
        method: "POST",
        body: formData
    });
}

async function saveProduct(event) {
    event.preventDefault();

    if (state.isBusy) return;

    let payload;

    try {
        payload = validateProductForm();
    } catch (error) {
        showMessage(error.message, "error");
        return;
    }

    const file = state.selectedImage;

    if (file) {
        const extension = file.name.split(".").pop()?.toLowerCase();

        if (!["jpg", "jpeg", "png", "webp"].includes(extension)) {
            showMessage("Поддерживаются только JPG, PNG и WebP.", "error");
            return;
        }

        if (file.size <= 0 || file.size > MAX_IMAGE_SIZE) {
            showMessage("Файл должен быть непустым и не превышать 5 МБ.", "error");
            return;
        }
    }

    setBusy(true);

    const editingId = state.editingProduct?.id ?? null;

    try {
        const result = await apiRequest(
            editingId
                ? `${PRODUCTS_URL}/${editingId}`
                : PRODUCTS_URL,
            {
                method: editingId ? "PUT" : "POST",
                headers: {
                    "Content-Type": "application/json"
                },
                body: JSON.stringify(payload)
            }
        );

        const savedId = editingId ?? extractProductId(result.data);

        let imageError = null;

        if (file && savedId) {
            try {
                await uploadProductImage(savedId, file);
            } catch (error) {
                imageError = error;
            }
        }

        resetProductForm();

        await loadProducts();

        if (imageError) {
            showMessage(
                `Данные товара сохранены, но фотография не загружена: ${imageError.message}`,
                "error"
            );
        } else if (file && !savedId) {
            showMessage(
                "Товар сохранён, но API не вернул его ID. Фотографию можно добавить после обновления списка и открытия товара на редактирование.",
                "error"
            );
        } else {
            showMessage(
                editingId
                    ? "Изменения товара сохранены."
                    : "Товар добавлен в каталог.",
                "success"
            );
        }
    } catch (error) {
        showMessage(error.message, "error");
    } finally {
        setBusy(false);
    }
}

async function removeCurrentImage() {
    if (!state.editingProduct || !state.currentImageUrl || state.isBusy) {
        return;
    }

    const confirmed = window.confirm(
        "Удалить фотографию этого товара?"
    );

    if (!confirmed) return;

    setBusy(true);

    try {
        await apiRequest(
            `${PRODUCTS_URL}/${state.editingProduct.id}/image`,
            {
                method: "DELETE"
            }
        );

        state.currentImageUrl = null;
        state.editingProduct.imageUrl = null;

        elements.removeImageButton.hidden = true;

        setImagePreview(null);

        await loadProducts();

        showMessage("Фотография удалена.", "success");
    } catch (error) {
        showMessage(error.message, "error");
    } finally {
        setBusy(false);
    }
}

// ==============================
// Удаление товара
// ==============================

async function deleteProduct(product) {
    const name = product.name ?? `товар №${product.id}`;

    const confirmed = window.confirm(
        `Удалить товар «${name}»? Это действие нельзя отменить.`
    );

    if (!confirmed || state.isBusy) return;

    setBusy(true);

    try {
        await apiRequest(`${PRODUCTS_URL}/${product.id}`, {
            method: "DELETE"
        });

        if (state.editingProduct?.id === product.id) {
            resetProductForm();
        }

        await loadProducts();

        showMessage(`Товар «${name}» удалён.`, "success");
    } catch (error) {
        showMessage(error.message, "error");
    } finally {
        setBusy(false);
    }
}

// ==============================
// Создание категории
// ==============================

async function addCategory(event) {
    event.preventDefault();

    if (state.isBusy) return;

    const name = elements.categoryName.value.trim();

    if (!name) {
        showMessage("Введите название категории.", "error");
        return;
    }

    setBusy(true);

    try {
        await apiRequest(CATEGORIES_URL, {
            method: "POST",
            headers: {
                "Content-Type": "application/json"
            },
            body: JSON.stringify({ name })
        });

        elements.categoryForm.reset();

        await loadCategories();

        showMessage(`Категория «${name}» добавлена.`, "success");
    } catch (error) {
        showMessage(error.message, "error");
    } finally {
        setBusy(false);
    }
}

// ==============================
// Переименование категории
// ==============================

async function renameCategory(category) {
    if (state.isBusy) return;

    const id = extractCategoryId(category);

    const currentName = category.name ?? category.Name ?? "";

    if (id === null) {
        showMessage("Не удалось определить ID категории.", "error");
        return;
    }

    const input = window.prompt(
        "Введите новое название категории:",
        currentName
    );

    if (input === null) return;

    const name = input.trim();

    if (name.length < 2 || name.length > 100) {
        showMessage(
            "Название должно содержать от 2 до 100 символов.",
            "error"
        );
        return;
    }

    if (name === String(currentName).trim()) {
        return;
    }

    setBusy(true);

    try {
        await apiRequest(`${CATEGORIES_URL}/${id}`, {
            method: "PUT",
            headers: {
                "Content-Type": "application/json"
            },
            body: JSON.stringify({ name })
        });

        await Promise.all([
            loadCategories(),
            loadProducts()
        ]);

        showMessage(
            `Категория переименована в «${name}».`,
            "success"
        );
    } catch (error) {
        showMessage(error.message, "error");
    } finally {
        setBusy(false);
    }
}

// ==============================
// Удаление категории
// ==============================

async function deleteCategory(category) {
    if (state.isBusy) return;

    const id = extractCategoryId(category);

    const name = category.name ?? category.Name ?? "Категория";

    if (id === null) {
        showMessage("Не удалось определить ID категории.", "error");
        return;
    }

    const confirmed = window.confirm(
        `Удалить категорию «${name}»?\n\n` +
        "Категорию с привязанными товарами удалить нельзя."
    );

    if (!confirmed) return;

    setBusy(true);

    try {
        await apiRequest(`${CATEGORIES_URL}/${id}`, {
            method: "DELETE"
        });

        await Promise.all([
            loadCategories(),
            loadProducts()
        ]);

        showMessage(`Категория «${name}» удалена.`, "success");
    } catch (error) {
        showMessage(error.message, "error");
    } finally {
        setBusy(false);
    }
}

// ==============================
// Управление состоянием загрузки
// ==============================

function setBusy(isBusy) {
    state.isBusy = isBusy;

    elements.saveProductButton.disabled = isBusy;
    elements.cancelEditButton.disabled = isBusy;
    elements.removeImageButton.disabled = isBusy;
    elements.refreshProductsButton.disabled = isBusy;
    elements.logoutButton.disabled = isBusy;

    for (const button of elements.categoryForm.querySelectorAll("button")) {
        button.disabled = isBusy;
    }

    for (const button of elements.categoryList.querySelectorAll("button")) {
        button.disabled = isBusy;
    }
}

// ==============================
// Обновление данных
// ==============================

async function refreshAll() {
    if (state.isBusy) return;

    setBusy(true);

    try {
        await Promise.all([
            loadProducts(),
            loadCategories()
        ]);

        showMessage("Данные каталога обновлены.", "success");
    } catch (error) {
        showMessage(error.message, "error");
    } finally {
        setBusy(false);
    }
}

// ==============================
// Авторизация и выход
// ==============================

function logout() {
    sessionStorage.removeItem(ACCESS_TOKEN_KEY);
    sessionStorage.removeItem(USERNAME_KEY);

    showAccessRequired(
        "Вы вышли из аккаунта. Для управления магазином войдите под учётной записью Admin."
    );
}

// ==============================
// Обработчики событий
// ==============================

function setupEventHandlers() {
    elements.productForm.addEventListener("submit", saveProduct);

    elements.cancelEditButton.addEventListener(
        "click",
        resetProductForm
    );

    elements.removeImageButton.addEventListener(
        "click",
        removeCurrentImage
    );

    elements.refreshProductsButton.addEventListener(
        "click",
        refreshAll
    );

    elements.productSearch.addEventListener(
        "input",
        renderProducts
    );

    elements.categoryForm.addEventListener(
        "submit",
        addCategory
    );

    elements.logoutButton.addEventListener(
        "click",
        logout
    );

    elements.productImage.addEventListener("change", () => {
        const file = elements.productImage.files?.[0] ?? null;

        state.selectedImage = file;

        if (!file) {
            setImagePreview(state.currentImageUrl);
            return;
        }

        if (file.size > MAX_IMAGE_SIZE) {
            showMessage("Файл превышает ограничение 5 МБ.", "error");

            elements.productImage.value = "";
            state.selectedImage = null;

            return;
        }

        const temporaryUrl = URL.createObjectURL(file);

        setImagePreview(temporaryUrl);

        state.previewObjectUrl = temporaryUrl;
    });
}

// ==============================
// Инициализация
// ==============================

async function initialize() {
    const token = readAccessToken();

    if (!token) {
        showAccessRequired(
            "Сначала войдите в аккаунт. После входа откройте эту страницу в той же вкладке."
        );

        return;
    }

    // Проверка роли на клиенте нужна только для интерфейса.
    // Реальная защита операций выполняется Backend через [Authorize(Roles = "Admin")].
    if (!tokenHasAdminRole(token)) {
        showAccessRequired(
            "У текущего аккаунта нет роли Admin. Операции управления доступны только администраторам."
        );

        return;
    }

    elements.accessCard.hidden = true;
    elements.app.hidden = false;

    setupEventHandlers();

    try {
        await Promise.all([
            loadProducts(),
            loadCategories()
        ]);

        showMessage("Вы вошли в панель управления.", "success");
    } catch (error) {
        showMessage(error.message, "error");
    }
}

initialize();