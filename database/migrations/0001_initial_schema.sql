-- 0001: initial schema. Never edit this script once committed; add a new script instead.

CREATE TABLE users (
    id            CHAR(36)      NOT NULL,
    email         VARCHAR(256)  NOT NULL,
    password_hash VARCHAR(512)  NOT NULL,
    role          VARCHAR(20)   NOT NULL,
    created_at    DATETIME(6)   NOT NULL,
    CONSTRAINT pk_users PRIMARY KEY (id),
    CONSTRAINT uq_users_email UNIQUE (email)
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4 COLLATE = utf8mb4_0900_ai_ci;

CREATE TABLE customers (
    id               CHAR(36)      NOT NULL,
    user_id          CHAR(36)      NOT NULL,
    full_name        VARCHAR(200)  NOT NULL,
    phone            VARCHAR(30)   NOT NULL,
    shipping_address VARCHAR(500)  NOT NULL,
    CONSTRAINT pk_customers PRIMARY KEY (id),
    CONSTRAINT uq_customers_user_id UNIQUE (user_id),
    CONSTRAINT fk_customers_users FOREIGN KEY (user_id) REFERENCES users (id)
        ON DELETE RESTRICT ON UPDATE RESTRICT
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4 COLLATE = utf8mb4_0900_ai_ci;

CREATE TABLE products (
    id             CHAR(36)       NOT NULL,
    sku            VARCHAR(32)    NOT NULL,
    name           VARCHAR(200)   NOT NULL,
    description    VARCHAR(2000)  NULL,
    price          DECIMAL(18,2)  NOT NULL,
    stock_quantity INT            NOT NULL,
    is_active      TINYINT(1)     NOT NULL,
    created_at     DATETIME(6)    NOT NULL,
    updated_at     DATETIME(6)    NOT NULL,
    CONSTRAINT pk_products PRIMARY KEY (id),
    CONSTRAINT uq_products_sku UNIQUE (sku),
    CONSTRAINT ck_products_price CHECK (price > 0),
    CONSTRAINT ck_products_stock_quantity CHECK (stock_quantity >= 0)
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4 COLLATE = utf8mb4_0900_ai_ci;

CREATE TABLE orders (
    id           CHAR(36)       NOT NULL,
    order_number VARCHAR(20)    NOT NULL,
    customer_id  CHAR(36)       NOT NULL,
    status       VARCHAR(20)    NOT NULL,
    total_amount DECIMAL(18,2)  NOT NULL,
    created_at   DATETIME(6)    NOT NULL,
    updated_at   DATETIME(6)    NOT NULL,
    CONSTRAINT pk_orders PRIMARY KEY (id),
    CONSTRAINT uq_orders_order_number UNIQUE (order_number),
    INDEX ix_orders_customer_id_created_at (customer_id, created_at),
    INDEX ix_orders_status (status),
    CONSTRAINT fk_orders_customers FOREIGN KEY (customer_id) REFERENCES customers (id)
        ON DELETE RESTRICT ON UPDATE RESTRICT
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4 COLLATE = utf8mb4_0900_ai_ci;

CREATE TABLE order_items (
    id           CHAR(36)       NOT NULL,
    order_id     CHAR(36)       NOT NULL,
    product_id   CHAR(36)       NOT NULL,
    product_name VARCHAR(200)   NOT NULL,
    unit_price   DECIMAL(18,2)  NOT NULL,
    quantity     INT            NOT NULL,
    line_total   DECIMAL(18,2)  NOT NULL,
    CONSTRAINT pk_order_items PRIMARY KEY (id),
    CONSTRAINT ck_order_items_quantity CHECK (quantity >= 1),
    CONSTRAINT fk_order_items_orders FOREIGN KEY (order_id) REFERENCES orders (id)
        ON DELETE CASCADE ON UPDATE RESTRICT,
    CONSTRAINT fk_order_items_products FOREIGN KEY (product_id) REFERENCES products (id)
        ON DELETE RESTRICT ON UPDATE RESTRICT
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4 COLLATE = utf8mb4_0900_ai_ci;

CREATE TABLE refresh_tokens (
    id                     CHAR(36)     NOT NULL,
    user_id                CHAR(36)     NOT NULL,
    token_hash             VARCHAR(64)  NOT NULL,
    expires_at             DATETIME(6)  NOT NULL,
    created_at             DATETIME(6)  NOT NULL,
    revoked_at             DATETIME(6)  NULL,
    replaced_by_token_hash VARCHAR(64)  NULL,
    CONSTRAINT pk_refresh_tokens PRIMARY KEY (id),
    CONSTRAINT uq_refresh_tokens_token_hash UNIQUE (token_hash),
    INDEX ix_refresh_tokens_user_id (user_id),
    CONSTRAINT fk_refresh_tokens_users FOREIGN KEY (user_id) REFERENCES users (id)
        ON DELETE RESTRICT ON UPDATE RESTRICT
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4 COLLATE = utf8mb4_0900_ai_ci;
