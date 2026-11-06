
console.log("JavaScript подключён!");

// Backend и Frontend работают с одного адреса.
const API_BASE_URL = "/api";
const API_URL = `${API_BASE_URL}/products`;

// Ключи хранения данных браузера.
const CART_STORAGE_KEY = "electronicsStoreCart";
const AUTH_STORAGE_KEY = "electronicsStoreAccessToken";
const AUTH_USERNAME_STORAGE_KEY = "electronicsStoreUsername";

// Корзина.
let cart = loadCart();

// Данные авторизации.
let accessToken = sessionStorage.getItem(AUTH_STORAGE_KEY);

let currentUsername = sessionStorage.getItem(
    AUTH_USERNAME_STORAGE_KEY
);

// Полученные товары каталога.
let productsCache = [];

// Сравнение товаров.
let comparisonProductIds = [];

const MAX_COMPARISON_PRODUCTS = 4;

// Состояние оформления заказа.
let isCheckoutInProgress = false;
let waitingForStockConfirmation = false;


// ==================================================
// ЗАГРУЗКА И СОХРАНЕНИЕ КОРЗИНЫ
// ==================================================

function loadCart() {
    try {
        const savedCart = localStorage.getItem(CART_STORAGE_KEY);

        if (!savedCart) {
            return [];
        }

        const parsedCart = JSON.parse(savedCart);

        if (!Array.isArray(parsedCart)) {
            return [];
        }

        return parsedCart
            .filter(item =>
                item &&
                Number.isInteger(Number(item.id)) &&
                Number(item.id) > 0 &&
                Number.isInteger(Number(item.cartQuantity)) &&
                Number(item.cartQuantity) > 0
            )
            .map(item => ({
                id: Number(item.id),
                name: String(item.name ?? "Товар"),
                price: Number(item.price) || 0,
                stock: Number.isFinite(Number(item.stock))
                    ? Number(item.stock)
                    : null,
                cartQuantity: Number(item.cartQuantity)
            }));

    } catch (error) {
        console.error("Ошибка загрузки корзины:", error);
        return [];
    }
}

function saveCart() {
    try {
        localStorage.setItem(
            CART_STORAGE_KEY,
            JSON.stringify(cart)
        );
    } catch (error) {
        console.error("Ошибка сохранения корзины:", error);
    }
}


// ==================================================
// ПОИСК
// ==================================================

function setupProductSearch() {
    const form = document.querySelector("#product-search-form");

    if (!form) {
        return;
    }

    form.addEventListener("submit", event => {
        event.preventDefault();

        const formData = new FormData(form);
        const params = new URLSearchParams(formData);

        window.location.href =
            `product.html?${params.toString()}`;
    });
}


// ==================================================
// ПОЛУЧЕНИЕ ТОВАРОВ ИЗ BACKEND
// ==================================================

async function getProducts() {
    // Загружаем каталог постранично, чтобы получить не только первые 10 товаров.
    const pageSize = 100;
    const firstResponse = await fetch(
        `${API_URL}?page=1&pageSize=${pageSize}`
    );

    if (!firstResponse.ok) {
        throw new Error(`Ошибка HTTP: ${firstResponse.status}`);
    }

    const firstPage = await firstResponse.json();
    const firstItems = firstPage.items ?? firstPage.Items;

    if (!Array.isArray(firstItems)) {
        throw new Error("Неверный формат данных от API");
    }

    const products = [...firstItems];
    const reportedTotalPages = Number(
        firstPage.totalPages ?? firstPage.TotalPages
    );
    const reportedTotalCount = Number(
        firstPage.totalCount ?? firstPage.TotalCount
    );

    // Обычно API возвращает totalPages. totalCount оставлен как запасной вариант.
    const totalPages = Number.isInteger(reportedTotalPages) && reportedTotalPages > 0
        ? reportedTotalPages
        : Number.isFinite(reportedTotalCount) && reportedTotalCount >= 0
            ? Math.ceil(reportedTotalCount / pageSize)
            : 1;

    for (let page = 2; page <= totalPages; page++) {
        const response = await fetch(
            `${API_URL}?page=${page}&pageSize=${pageSize}`
        );

        if (!response.ok) {
            throw new Error(`Ошибка HTTP при загрузке страницы ${page}: ${response.status}`);
        }

        const data = await response.json();
        const items = data.items ?? data.Items;

        if (!Array.isArray(items)) {
            throw new Error(`Неверный формат данных API на странице ${page}`);
        }

        products.push(...items);
    }

    return products;
}


// Обновляем информацию о товарах, которые уже находятся в корзине.

function syncCartWithProducts(products) {
    const productsById = new Map(
        products.map(product => [
            Number(product.id),
            product
        ])
    );

    cart = cart.map(item => {
        const product = productsById.get(item.id);

        if (!product) {
            return item;
        }

        return {
            ...item,
            name: product.name,
            price: Number(product.price),
            stock: Number(product.quantity)
        };
    });

    saveCart();
}


// ==================================================
// ФИЛЬТРАЦИЯ И СОРТИРОВКА
// ==================================================

function filterProducts(products) {
    const params = new URLSearchParams(window.location.search);

    const search = (params.get("search") ?? "")
        .trim()
        .toLowerCase();

    const category = params.get("category") ?? "all";
    const inStock = params.get("inStock") === "true";
    const sort = params.get("sort") ?? "price-asc";

    let result = [...products];

    // Поиск по названию, описанию и категории.
    if (search) {
        result = result.filter(product => {
            const searchableText = [
                product.name,
                product.description,
                product.categoryName
            ]
                .filter(Boolean)
                .join(" ")
                .toLowerCase();

            return searchableText.includes(search);
        });
    }

    // Фильтр по категории.
    const categoryNames = {
        laptops: "Ноутбуки",
        smartphones: "Смартфоны",
        accessories: "Кабели и адаптеры"
    };

    if (category !== "all") {
        const selectedCategory = categoryNames[category];

        if (selectedCategory) {
            result = result.filter(
                product => product.categoryName === selectedCategory
            );
        }
    }

    // Только товары в наличии.
    if (inStock) {
        result = result.filter(
            product => Number(product.quantity) > 0
        );
    }

    // Сортировка по цене.
    if (sort === "price-asc") {
        result.sort(
            (a, b) => Number(a.price) - Number(b.price)
        );

    } else if (sort === "price-desc") {
        result.sort(
            (a, b) => Number(b.price) - Number(a.price)
        );
    }

    return result;
}


// ==================================================
// ОПРЕДЕЛЕНИЕ КАТЕГОРИИ ТОВАРА
// ==================================================

function getProductCategoryKey(product) {
    const rawCategoryId =
        product.categoryId ?? product.CategoryId;

    if (
        rawCategoryId !== null &&
        rawCategoryId !== undefined &&
        String(rawCategoryId).trim() !== ""
    ) {
        const categoryId = Number(rawCategoryId);

        if (
            Number.isInteger(categoryId) &&
            categoryId > 0
        ) {
            return `id:${categoryId}`;
        }
    }

    const categoryName = String(
        product.categoryName ?? product.CategoryName ?? ""
    )
        .trim()
        .toLocaleLowerCase("ru-RU");

    return categoryName
        ? `name:${categoryName}`
        : "uncategorized";
}


// ==================================================
// ПОЛУЧЕНИЕ ТОВАРОВ ДЛЯ СРАВНЕНИЯ
// ==================================================

function getComparisonProducts() {
    return comparisonProductIds
        .map(id => productsCache.find(
            product => Number(product.id) === id
        ))
        .filter(Boolean);
}


// ==================================================
// СООБЩЕНИЯ О СРАВНЕНИИ
// ==================================================

function showComparisonMessage(message) {
    const element = document.querySelector("#comparison-status");

    if (element) {
        element.textContent = message;
    }
}


// ==================================================
// ДОБАВЛЕНИЕ ТОВАРА В СРАВНЕНИЕ
// ==================================================

function addToComparison(product) {
    const id = Number(product.id);

    if (!Number.isInteger(id) || id <= 0) {
        showComparisonMessage(
            "Не удалось определить ID товара."
        );
        return;
    }

    if (comparisonProductIds.includes(id)) {
        return;
    }

    const selectedProducts = getComparisonProducts();

    // Ограничиваем количество товаров.
    if (selectedProducts.length >= MAX_COMPARISON_PRODUCTS) {
        showComparisonMessage(
            `Можно сравнивать не более ${MAX_COMPARISON_PRODUCTS} товаров одновременно.`
        );
        return;
    }

    // Проверяем совпадение категорий.
    if (
        selectedProducts.length > 0 &&
        getProductCategoryKey(selectedProducts[0]) !==
        getProductCategoryKey(product)
    ) {
        showComparisonMessage(
            "Нельзя сравнивать товары разных категорий. Выберите товар той же категории или очистите сравнение."
        );
        return;
    }

    comparisonProductIds.push(id);

    renderComparison();

    // Обновляем кнопки в карточках.
    renderProducts(filterProducts(productsCache));

    showComparisonMessage(
        `Товар «${product.name}» добавлен к сравнению.`
    );
}


// ==================================================
// УДАЛЕНИЕ ОДНОГО ТОВАРА ИЗ СРАВНЕНИЯ
// ==================================================

function removeFromComparison(productId) {
    comparisonProductIds = comparisonProductIds.filter(
        id => id !== Number(productId)
    );

    renderComparison();

    renderProducts(filterProducts(productsCache));
}


// ==================================================
// ОЧИСТКА СРАВНЕНИЯ
// ==================================================

function clearComparison() {
    comparisonProductIds = [];

    renderComparison();

    renderProducts(filterProducts(productsCache));

    showComparisonMessage(
        "Список сравнения очищен."
    );
}


// ==================================================
// ПОЛУЧЕНИЕ И ФОРМАТИРОВАНИЕ ХАРАКТЕРИСТИК
// ==================================================

function getProductSpecifications(product) {
    const specifications =
        product.specifications ?? product.Specifications;

    return Array.isArray(specifications)
        ? specifications
        : [];
}

function getSpecificationField(specification, fieldName) {
    if (!specification) {
        return undefined;
    }

    const pascalCaseName =
        fieldName.charAt(0).toUpperCase() + fieldName.slice(1);

    return specification[fieldName] ?? specification[pascalCaseName];
}

function getSpecificationKey(specification) {
    const code = getSpecificationField(specification, "code");

    if (code !== null && code !== undefined && String(code).trim()) {
        return `code:${String(code).trim().toLowerCase()}`;
    }

    const categorySpecificationId = getSpecificationField(
        specification,
        "categorySpecificationId"
    );

    if (
        categorySpecificationId !== null &&
        categorySpecificationId !== undefined
    ) {
        return `id:${categorySpecificationId}`;
    }

    const name = getSpecificationField(specification, "name");

    return name
        ? `name:${String(name).trim().toLowerCase()}`
        : null;
}

function formatSpecificationValue(specification) {
    const rawValue = getSpecificationField(specification, "value");

    if (rawValue === null || rawValue === undefined || String(rawValue).trim() === "") {
        return "—";
    }

    const value = String(rawValue).trim();
    const unit = getSpecificationField(specification, "unit");
    const normalizedValue = value.toLocaleLowerCase("ru-RU");
    const noUnitValues = [
        "—",
        "не применимо",
        "не требуется",
        "не поддерживается",
        "нет"
    ];

    if (
        !unit ||
        noUnitValues.includes(normalizedValue) ||
        value.endsWith(String(unit))
    ) {
        return value;
    }

    return `${value} ${unit}`;
}

function getComparisonSpecificationDefinitions(products) {
    const definitionsByKey = new Map();

    products.forEach(product => {
        getProductSpecifications(product).forEach(specification => {
            const key = getSpecificationKey(specification);

            if (!key || definitionsByKey.has(key)) {
                return;
            }

            definitionsByKey.set(key, {
                key,
                name: getSpecificationField(specification, "name") ?? "Характеристика",
                sortOrder: Number(
                    getSpecificationField(specification, "sortOrder") ?? 0
                )
            });
        });
    });

    return [...definitionsByKey.values()].sort((a, b) => {
        if (a.sortOrder !== b.sortOrder) {
            return a.sortOrder - b.sortOrder;
        }

        return String(a.name).localeCompare(String(b.name), "ru");
    });
}

function getProductSpecificationValue(product, definitionKey) {
    const specification = getProductSpecifications(product).find(
        item => getSpecificationKey(item) === definitionKey
    );

    return specification
        ? formatSpecificationValue(specification)
        : "—";
}


// ==================================================
// СОЗДАНИЕ ТАБЛИЦЫ СРАВНЕНИЯ
// ==================================================

function renderComparison() {
    const head = document.querySelector("#comparison-table-head");
    const body = document.querySelector("#product-table-body");
    const wrapper = document.querySelector("#comparison-table-wrap");
    const status = document.querySelector("#comparison-status");
    const clearButton = document.querySelector("#clear-comparison-button");

    if (!head || !body || !wrapper || !status || !clearButton) {
        return;
    }

    const selectedProducts = getComparisonProducts();

    head.replaceChildren();
    body.replaceChildren();

    clearButton.hidden = selectedProducts.length === 0;

    if (selectedProducts.length === 0) {
        wrapper.hidden = true;
        status.textContent =
            "Вы пока не выбрали товары. Нажмите «Сравнить» в карточке товара.";
        return;
    }

    if (selectedProducts.length === 1) {
        wrapper.hidden = true;
        status.textContent =
            `Выбран товар «${selectedProducts[0].name ?? selectedProducts[0].Name ?? "Товар"}». Добавьте ещё один товар этой же категории.`;
        return;
    }

    wrapper.hidden = false;

    const categoryName =
        selectedProducts[0].categoryName ??
        selectedProducts[0].CategoryName ??
        "Без категории";

    status.textContent =
        `Сравнение ${selectedProducts.length} товаров. Категория: ${categoryName}.`;

    // Заголовки таблицы.
    const headerRow = document.createElement("tr");
    const parameterHeader = document.createElement("th");

    parameterHeader.scope = "col";
    parameterHeader.textContent = "Характеристика";
    headerRow.append(parameterHeader);

    selectedProducts.forEach(product => {
        const cell = document.createElement("th");
        cell.scope = "col";

        const heading = document.createElement("div");
        heading.className = "comparison-product-heading";

        const productName = document.createElement("span");
        productName.textContent =
            product.name ?? product.Name ?? "Товар";

        const removeButton = document.createElement("button");
        removeButton.type = "button";
        removeButton.className = "comparison-remove-button";
        removeButton.textContent = "Убрать";
        removeButton.addEventListener("click", () => {
            removeFromComparison(product.id ?? product.Id);
        });

        heading.append(productName, removeButton);
        cell.append(heading);
        headerRow.append(cell);
    });

    head.append(headerRow);

    // Основные поля товара.
    const specifications = [
        {
            name: "Фотография",
            value: product => product.imageUrl ?? product.ImageUrl
        },
        {
            name: "Категория",
            value: product =>
                product.categoryName ?? product.CategoryName ?? "Без категории"
        },
        {
            name: "Цена",
            value: product =>
                `${Number(product.price ?? product.Price).toFixed(2)} BYN`
        },
        {
            name: "Старая цена",
            value: product => {
                const oldPrice = Number(product.oldPrice ?? product.OldPrice);
                const currentPrice = Number(product.price ?? product.Price);

                return Number.isFinite(oldPrice) && oldPrice > currentPrice
                    ? `${oldPrice.toFixed(2)} BYN`
                    : "—";
            }
        },
        {
            name: "Наличие",
            value: product => {
                const quantity = Number(product.quantity ?? product.Quantity);

                if (!Number.isFinite(quantity)) {
                    return "Не указано";
                }

                return quantity > 0 ? `${quantity} шт.` : "Нет в наличии";
            }
        },
        {
            name: "Описание",
            value: product =>
                (product.description ?? product.Description ?? "").trim() ||
                "Описание отсутствует"
        }
    ];

    specifications.forEach(specification => {
        const row = document.createElement("tr");
        const label = document.createElement("th");

        label.scope = "row";
        label.textContent = specification.name;
        row.append(label);

        selectedProducts.forEach(product => {
            const cell = document.createElement("td");
            const value = specification.value(product);

            if (specification.name === "Фотография" && value) {
                const image = document.createElement("img");
                image.className = "comparison-product-image";
                image.src = value;
                image.alt = product.name ?? product.Name ?? "Фотография товара";
                image.loading = "lazy";

                image.addEventListener("error", () => {
                    cell.textContent = "Нет фотографии";
                }, { once: true });

                cell.append(image);
            } else {
                cell.textContent = value || "—";
            }

            row.append(cell);
        });

        body.append(row);
    });

    // Технические характеристики, заданные для выбранной категории.
    const technicalDefinitions =
        getComparisonSpecificationDefinitions(selectedProducts);

    technicalDefinitions.forEach(definition => {
        const row = document.createElement("tr");
        const label = document.createElement("th");

        label.scope = "row";
        label.textContent = definition.name;
        row.append(label);

        selectedProducts.forEach(product => {
            const cell = document.createElement("td");
            cell.textContent = getProductSpecificationValue(
                product,
                definition.key
            );
            row.append(cell);
        });

        body.append(row);
    });
}

// ==================================================
// ОТОБРАЖЕНИЕ ТОВАРОВ
// ==================================================

function renderProducts(products) {
    const container = document.querySelector("#products-list");

    if (!container) {
        console.error("Не найден контейнер products-list");
        return;
    }

    container.replaceChildren();

    if (products.length === 0) {
        container.textContent = "Товары не найдены.";
        return;
    }

    const selectedProducts = getComparisonProducts();

    products.forEach(product => {
        const stock = Number(product.quantity);
        const isInStock = stock > 0;

        // Карточка товара.
        const article = document.createElement("article");

        article.className = "product-card";

        // Фотография товара.
        const imageUrl = typeof product.imageUrl === "string"
            ? product.imageUrl.trim()
            : "";

        if (imageUrl) {
            const image = document.createElement("img");

            image.className = "product-card__image";
            image.src = imageUrl;
            image.alt = `Фотография товара: ${product.name}`;
            image.loading = "lazy";
            image.decoding = "async";

            image.addEventListener("error", () => {
                console.error(
                    `Не удалось загрузить изображение товара ${product.id}:`,
                    imageUrl
                );

                image.alt = "Не удалось загрузить изображение товара";
            }, { once: true });

            article.append(image);
        }

        // Название.
        const name = document.createElement("h3");

        name.textContent = product.name ?? "Без названия";

        // Описание.
        const description = document.createElement("p");

        description.textContent =
            product.description ?? "Описание отсутствует";

        // Категория.
        const category = document.createElement("p");

        category.textContent =
            product.categoryName ?? "Категория не указана";

        // Цена.
        const price = document.createElement("p");

        price.className = "product-price";

        price.textContent =
            `${Number(product.price).toFixed(2)} BYN`;

        // Наличие.
        const stockStatus = document.createElement("p");

        stockStatus.className = isInStock
            ? "stock-status in-stock"
            : "stock-status out-of-stock";

        stockStatus.textContent = isInStock
            ? `В наличии: ${stock} шт.`
            : "Товар отсутствует на складе.";

        // ==========================================
        // КНОПКА СРАВНЕНИЯ
        // ==========================================

        const compareButton = document.createElement("button");

        compareButton.type = "button";
        compareButton.className = "product-compare-button";

        const productId = Number(product.id);

        const alreadySelected =
            comparisonProductIds.includes(productId);

        const categoryMismatch =
            selectedProducts.length > 0 &&
            getProductCategoryKey(selectedProducts[0]) !==
            getProductCategoryKey(product);

        const limitReached =
            comparisonProductIds.length >= MAX_COMPARISON_PRODUCTS;

        if (alreadySelected) {
            compareButton.textContent = "В сравнении";
            compareButton.disabled = true;

        } else if (limitReached) {
            compareButton.textContent = "Достигнут лимит";
            compareButton.disabled = true;

        } else if (categoryMismatch) {
            compareButton.textContent = "Другая категория";

            // Кнопка остаётся активной, чтобы пользователь
            // мог получить объяснение причины отказа.

        } else {
            compareButton.textContent = "Сравнить";
        }

        compareButton.addEventListener("click", () => {
            addToComparison(product);
        });

        // ==========================================
        // КНОПКА КОРЗИНЫ
        // ==========================================

        const addButton = document.createElement("button");

        addButton.type = "button";

        addButton.textContent = isInStock
            ? "Добавить в корзину"
            : "Нет в наличии";

        addButton.disabled = !isInStock;

        addButton.addEventListener("click", () => {
            addToCart(product);
        });

        // Добавляем основные элементы в карточку.
        article.append(
            name,
            description,
            category,
            price,
            stockStatus,
            compareButton,
            addButton
        );

        // Дополнительный блок технических характеристик.
        const productSpecifications = getProductSpecifications(product);

        if (productSpecifications.length > 0) {
            const details = document.createElement("details");
            details.className = "product-specifications";

            const summary = document.createElement("summary");
            summary.textContent = "Технические характеристики";

            const list = document.createElement("dl");

            productSpecifications
                .slice()
                .sort((a, b) =>
                    Number(getSpecificationField(a, "sortOrder") ?? 0) -
                    Number(getSpecificationField(b, "sortOrder") ?? 0)
                )
                .forEach(specification => {
                    const term = document.createElement("dt");
                    term.textContent =
                        getSpecificationField(specification, "name") ?? "Характеристика";

                    const value = document.createElement("dd");
                    value.textContent = formatSpecificationValue(specification);

                    list.append(term, value);
                });

            details.append(summary, list);
            article.append(details);
        } else {
            const details = document.createElement("details");
            details.className = "product-specifications";

            const summary = document.createElement("summary");
            summary.textContent = "Технические характеристики";

            const message = document.createElement("p");
            message.textContent = "Характеристики не указаны.";

            details.append(summary, message);
            article.append(details);
        }

        container.append(article);
    });

    console.log("Карточки товаров созданы:", products.length);
}


// ==================================================
// ОСНОВНАЯ ЗАГРУЗКА КАТАЛОГА
// ==================================================

async function loadProductsFromApi() {
    const container = document.querySelector("#products-list");

    if (!container) {
        return;
    }

    try {
        container.textContent = "Загрузка товаров...";

        const products = await getProducts();

        productsCache = products;

        // Обновляем остатки товаров в корзине.
        syncCartWithProducts(products);

        // Убираем из сравнения товары, которых больше нет в каталоге.
        comparisonProductIds = comparisonProductIds.filter(id =>
            productsCache.some(
                product => Number(product.id) === id
            )
        );

        const filteredProducts = filterProducts(products);

        renderProducts(filteredProducts);

        renderComparison();

        renderCart();

        console.log("Загружено товаров:", products.length);
        console.log("После фильтрации:", filteredProducts.length);

    } catch (error) {
        console.error("Ошибка загрузки товаров:", error);

        container.textContent =
            "Не удалось загрузить товары.";
    }
}


// ==================================================
// КОРЗИНА
// ==================================================

function addToCart(product) {
    const stock = Number(product.quantity);

    // Дополнительная защита от добавления отсутствующего товара.
    if (!Number.isFinite(stock) || stock <= 0) {
        alert("Товар отсутствует на складе.");
        return;
    }

    const existingItem = cart.find(
        item => item.id === Number(product.id)
    );

    if (existingItem) {
        existingItem.stock = stock;

        if (existingItem.cartQuantity >= stock) {
            alert("Больше товаров нет в наличии.");
            return;
        }

        existingItem.cartQuantity++;

    } else {
        cart.push({
            id: Number(product.id),
            name: product.name,
            price: Number(product.price),
            stock: stock,
            cartQuantity: 1
        });
    }

    renderCart();
}

function changeCartQuantity(productId, change) {
    const item = cart.find(
        product => product.id === Number(productId)
    );

    if (!item) {
        return;
    }

    const newQuantity = item.cartQuantity + change;

    if (newQuantity <= 0) {
        cart = cart.filter(
            product => product.id !== Number(productId)
        );

    } else if (
        change > 0 &&
        item.stock !== null &&
        newQuantity > item.stock
    ) {
        showOrderMessage(
            `Недостаточно товара "${item.name}" на складе.`
        );

        return;

    } else {
        item.cartQuantity = newQuantity;
    }

    renderCart();
}

function renderCart() {
    const container = document.querySelector("#cart-items");
    const totalElement = document.querySelector("#cart-total");

    saveCart();

    updateCheckoutButton();

    if (!container || !totalElement) {
        return;
    }

    container.replaceChildren();

    if (cart.length === 0) {
        const emptyMessage = document.createElement("p");

        emptyMessage.textContent = "Корзина пуста.";

        container.append(emptyMessage);

        totalElement.textContent = "Итого: 0.00 BYN";

        return;
    }

    cart.forEach(item => {
        const row = document.createElement("div");

        row.className = "cart-item";

        const name = document.createElement("p");

        const itemTotal = item.price * item.cartQuantity;

        name.textContent =
            `${item.name} — ${item.price.toFixed(2)} BYN × ${item.cartQuantity} = ${itemTotal.toFixed(2)} BYN`;

        // Информация о доступности товара.
        const availability = document.createElement("p");

        if (item.stock === null) {
            availability.textContent =
                "Наличие будет проверено при оформлении.";

        } else if (item.stock === 0) {
            availability.textContent =
                "Предупреждение: товар закончился.";

        } else if (item.cartQuantity > item.stock) {
            availability.textContent =
                `В наличии только ${item.stock} шт. Нужно скорректировать количество.`;

        } else {
            availability.textContent =
                `Доступно на складе: ${item.stock} шт.`;
        }

        availability.className = "cart-availability";

        // Уменьшение количества.
        const decreaseButton = document.createElement("button");

        decreaseButton.type = "button";
        decreaseButton.textContent = "−";

        decreaseButton.addEventListener("click", () => {
            changeCartQuantity(item.id, -1);
        });

        // Увеличение количества.
        const increaseButton = document.createElement("button");

        increaseButton.type = "button";
        increaseButton.textContent = "+";

        increaseButton.disabled =
            item.stock !== null &&
            item.cartQuantity >= item.stock;

        increaseButton.addEventListener("click", () => {
            changeCartQuantity(item.id, 1);
        });

        row.append(
            name,
            availability,
            decreaseButton,
            increaseButton
        );

        container.append(row);
    });

    const total = cart.reduce(
        (sum, item) =>
            sum + item.price * item.cartQuantity,
        0
    );

    totalElement.textContent =
        `Итого: ${total.toFixed(2)} BYN`;
}


// ==================================================
// АВТОРИЗАЦИЯ
// ==================================================

function updateAuthUI() {
    const status = document.querySelector("#auth-status");
    const loginForm = document.querySelector("#login-form");
    const logoutButton = document.querySelector("#logout-button");

    const isLoggedIn = Boolean(accessToken);

    if (status) {
        status.textContent = isLoggedIn
            ? `Вы вошли как ${currentUsername ?? "пользователь"}.`
            : "Вы не авторизованы.";
    }

    if (loginForm) {
        loginForm.hidden = isLoggedIn;
    }

    if (logoutButton) {
        logoutButton.hidden = !isLoggedIn;
    }

    updateCheckoutButton();
}

function showAuthMessage(message) {
    const messageElement = document.querySelector("#auth-message");

    if (messageElement) {
        messageElement.textContent = message;
    }
}

function setupLogin() {
    const form = document.querySelector("#login-form");

    if (!form) {
        return;
    }

    form.addEventListener("submit", async event => {
        event.preventDefault();

        const submitButton =
            form.querySelector('button[type="submit"]');

        const username = form.elements.username.value.trim();
        const password = form.elements.password.value;

        if (submitButton) {
            submitButton.disabled = true;
        }

        showAuthMessage("Выполняется вход...");

        try {
            const response = await fetch(
                `${API_BASE_URL}/auth/login`,
                {
                    method: "POST",
                    headers: {
                        "Content-Type": "application/json"
                    },
                    body: JSON.stringify({
                        username,
                        password
                    })
                }
            );

            const data = await response.json().catch(() => null);

            if (!response.ok) {
                throw new Error(
                    data?.message ?? "Не удалось выполнить вход."
                );
            }

            if (!data?.accessToken) {
                throw new Error(
                    "Backend не вернул токен авторизации."
                );
            }

            accessToken = data.accessToken;
            currentUsername = username;

            sessionStorage.setItem(
                AUTH_STORAGE_KEY,
                accessToken
            );

            sessionStorage.setItem(
                AUTH_USERNAME_STORAGE_KEY,
                currentUsername
            );

            form.reset();

            updateAuthUI();

            showAuthMessage("Вход выполнен успешно.");

        } catch (error) {
            console.error("Ошибка авторизации:", error);

            showAuthMessage(error.message);

        } finally {
            if (submitButton) {
                submitButton.disabled = false;
            }

            updateCheckoutButton();
        }
    });
}

function setupLogout() {
    const logoutButton =
        document.querySelector("#logout-button");

    if (!logoutButton) {
        return;
    }

    logoutButton.addEventListener("click", () => {
        accessToken = null;
        currentUsername = null;

        sessionStorage.removeItem(AUTH_STORAGE_KEY);
        sessionStorage.removeItem(AUTH_USERNAME_STORAGE_KEY);

        updateAuthUI();

        showAuthMessage("Вы вышли из аккаунта.");
    });
}


// ==================================================
// ОФОРМЛЕНИЕ ЗАКАЗА
// ==================================================

function updateCheckoutButton() {
    const button = document.querySelector("#checkout-button");

    if (!button) {
        return;
    }

    if (isCheckoutInProgress) {
        button.textContent = "Оформляем заказ...";
        button.disabled = true;
        return;
    }

    if (waitingForStockConfirmation) {
        button.textContent = "Ожидается подтверждение изменений";
        button.disabled = true;
        return;
    }

    if (!accessToken) {
        button.textContent = "Войдите, чтобы оформить заказ";
        button.disabled = true;
        return;
    }

    if (cart.length === 0) {
        button.textContent = "Корзина пуста";
        button.disabled = true;
        return;
    }

    button.textContent = "Оформить заказ";
    button.disabled = false;
}

function showOrderMessage(message) {
    const container = document.querySelector("#order-result");

    if (!container) {
        return;
    }

    container.replaceChildren();

    const paragraph = document.createElement("p");

    paragraph.textContent = message;

    container.append(paragraph);
}


// ==================================================
// ПРЕДУПРЕЖДЕНИЕ О НЕДОСТАТОЧНОМ КОЛИЧЕСТВЕ
// ==================================================

// Формируем предполагаемую скорректированную корзину.
// Исходную корзину пока не изменяем.

function getAdjustedCart(stockItems) {
    const stockByProductId = new Map(
        stockItems.map(item => [
            Number(item.productId),
            item
        ])
    );

    return cart.flatMap(item => {
        const problem = stockByProductId.get(item.id);

        if (!problem) {
            return [item];
        }

        const availableQuantity =
            Number(problem.availableQuantity);

        if (
            !Number.isInteger(availableQuantity) ||
            availableQuantity <= 0
        ) {
            return [];
        }

        return [{
            ...item,
            stock: availableQuantity,
            cartQuantity: Math.min(
                item.cartQuantity,
                availableQuantity
            )
        }];
    });
}


// Показываем пользователю проблемные товары.

function showStockAdjustmentPrompt(stockItems) {
    const container = document.querySelector("#order-result");

    if (!container) {
        return;
    }

    waitingForStockConfirmation = true;

    updateCheckoutButton();

    container.replaceChildren();

    const heading = document.createElement("h2");

    heading.textContent = "Изменилось наличие товаров";

    const description = document.createElement("p");

    description.textContent =
        "Некоторых товаров недостаточно на складе. Заказ пока не создан, а корзина не изменена.";

    const list = document.createElement("ul");

    stockItems.forEach(item => {
        const listItem = document.createElement("li");

        const available = Number(item.availableQuantity);
        const requested = Number(item.requestedQuantity);

        const productName = item.productName ?? "Товар";

        let recommendation;

        if (available <= 0) {
            recommendation =
                "Товар отсутствует на складе. Предлагаем удалить его из корзины.";
        } else {
            recommendation =
                `Запрошено: ${requested} шт. Доступно: ${available} шт. Предлагаем изменить количество на ${available}.`;
        }

        listItem.textContent =
            `${productName}: ${recommendation}`;

        list.append(listItem);
    });

    const explanation = document.createElement("p");

    explanation.textContent =
        "Вы можете подтвердить предложенные изменения или оставить корзину без изменений. При подтверждении Backend повторно проверит остатки.";

    const confirmButton = document.createElement("button");

    confirmButton.type = "button";

    confirmButton.textContent =
        "Подтвердить изменения и продолжить";

    confirmButton.addEventListener("click", async () => {
        // Изменяем корзину только после нажатия пользователя.
        cart = getAdjustedCart(stockItems);

        waitingForStockConfirmation = false;

        renderCart();

        if (cart.length === 0) {
            showOrderMessage(
                "После корректировки корзина оказалась пустой. Заказ не оформлен."
            );

            return;
        }

        showOrderMessage(
            "Корзина скорректирована. Повторно проверяем наличие товаров..."
        );

        await submitOrder();
    });

    const cancelButton = document.createElement("button");

    cancelButton.type = "button";

    cancelButton.textContent =
        "Отказаться и оставить корзину";

    cancelButton.addEventListener("click", () => {
        waitingForStockConfirmation = false;

        updateCheckoutButton();

        showOrderMessage(
            "Оформление отменено. Корзина оставлена без изменений."
        );
    });

    container.append(
        heading,
        description,
        list,
        explanation,
        confirmButton,
        cancelButton
    );
}


// ==================================================
// РЕЗУЛЬТАТ УСПЕШНОГО ОФОРМЛЕНИЯ
// ==================================================

function renderOrderResult(order) {
    const container = document.querySelector("#order-result");

    if (!container) {
        return;
    }

    container.replaceChildren();

    const heading = document.createElement("h2");

    heading.textContent = `Заказ №${order.id}`;

    const status = document.createElement("p");

    status.textContent = `Статус: ${order.status}`;

    const date = document.createElement("p");

    date.textContent =
        `Дата: ${new Date(order.createdAtUtc).toLocaleString("ru-RU")}`;

    const total = document.createElement("p");

    total.textContent =
        `Итого: ${Number(order.totalAmount).toFixed(2)} BYN`;

    const itemsHeading = document.createElement("h3");

    itemsHeading.textContent = "Состав заказа";

    const list = document.createElement("ul");

    (order.items ?? []).forEach(item => {
        const listItem = document.createElement("li");

        listItem.textContent =
            `${item.productName} — ${item.quantity} шт. × ${Number(item.unitPrice).toFixed(2)} BYN = ${Number(item.lineTotal).toFixed(2)} BYN`;

        list.append(listItem);
    });

    container.append(
        heading,
        status,
        date,
        total,
        itemsHeading,
        list
    );
}


// ==================================================
// ОТПРАВКА ЗАКАЗА
// ==================================================

async function submitOrder() {
    if (isCheckoutInProgress) {
        return;
    }

    if (!accessToken) {
        showOrderMessage("Сначала войдите в аккаунт.");
        return;
    }

    if (cart.length === 0) {
        showOrderMessage("Добавьте товары в корзину.");
        return;
    }

    // Отправляем только ID товаров и количество.
    // Цены и названия Backend получает из БД.

    const orderRequest = {
        items: cart.map(item => ({
            productId: Number(item.id),
            quantity: Number(item.cartQuantity)
        }))
    };

    isCheckoutInProgress = true;

    updateCheckoutButton();

    showOrderMessage("Отправляем заказ на сервер...");

    try {
        const response = await fetch(
            `${API_BASE_URL}/orders`,
            {
                method: "POST",
                headers: {
                    "Content-Type": "application/json",
                    Authorization: `Bearer ${accessToken}`
                },
                body: JSON.stringify(orderRequest)
            }
        );

        const data = await response.json().catch(() => null);

        // Проверяем авторизацию.
        if (response.status === 401) {
            accessToken = null;
            currentUsername = null;

            sessionStorage.removeItem(AUTH_STORAGE_KEY);
            sessionStorage.removeItem(AUTH_USERNAME_STORAGE_KEY);

            updateAuthUI();

            throw new Error(
                "Сессия завершена. Войдите в аккаунт заново."
            );
        }

        // Backend обнаружил недостаток товаров.
        if (
            response.status === 409 &&
            Array.isArray(data?.items) &&
            data.items.length > 0
        ) {
            showStockAdjustmentPrompt(data.items);
            return;
        }

        // Другие ошибки.
        if (!response.ok) {
            throw new Error(
                data?.message ??
                data?.title ??
                `Ошибка оформления заказа: HTTP ${response.status}`
            );
        }

        // Заказ успешно создан.
        renderOrderResult(data);

        // Очищаем корзину только после подтверждения Backend.
        cart = [];

        renderCart();

        // Обновляем остатки в каталоге.
        // Ошибка обновления каталога не должна отменять успешный заказ.
        await loadProductsFromApi();

    } catch (error) {
        console.error("Ошибка оформления заказа:", error);

        showOrderMessage(error.message);

    } finally {
        isCheckoutInProgress = false;

        updateCheckoutButton();
    }
}


function setupCheckout() {
    const button = document.querySelector("#checkout-button");

    if (!button) {
        return;
    }

    button.addEventListener("click", async () => {
        await submitOrder();
    });
}


// ==================================================
// ПОДКЛЮЧЕНИЕ СРАВНЕНИЯ
// ==================================================

function setupComparison() {
    const clearButton =
        document.querySelector("#clear-comparison-button");

    if (clearButton) {
        clearButton.addEventListener(
            "click",
            clearComparison
        );
    }

    renderComparison();
}


// ==================================================
// ЗАПУСК ПРИЛОЖЕНИЯ
// ==================================================

setupProductSearch();

setupLogin();

setupLogout();

setupCheckout();

setupComparison();

updateAuthUI();

loadProductsFromApi();

renderCart();
