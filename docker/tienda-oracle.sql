-- Datos de ejemplo de una tienda para Oracle 11g XE (usuario TIENDA / TIENDA).
-- Se ejecuta como SYSTEM. Vuelve a crear el usuario si ya existe.
WHENEVER SQLERROR CONTINUE
DROP USER tienda CASCADE;
WHENEVER SQLERROR EXIT FAILURE

CREATE USER tienda IDENTIFIED BY tienda DEFAULT TABLESPACE users QUOTA UNLIMITED ON users;
GRANT CREATE SESSION, CREATE TABLE TO tienda;

CONNECT tienda/tienda@//localhost:1521/XE

CREATE TABLE clientes (
    id_cliente      NUMBER(10)     NOT NULL PRIMARY KEY,
    nombre_cliente  VARCHAR2(100)  NOT NULL,
    email           VARCHAR2(150),
    ciudad          VARCHAR2(60)
);

CREATE TABLE productos (
    id_producto      NUMBER(10)     NOT NULL PRIMARY KEY,
    nombre_producto  VARCHAR2(100)  NOT NULL,
    categoria        VARCHAR2(40)   NOT NULL,
    precio           NUMBER(10,2)   NOT NULL
);

CREATE TABLE pedidos (
    id_pedido     NUMBER(10)    NOT NULL PRIMARY KEY,
    id_cliente    NUMBER(10)    NOT NULL REFERENCES clientes (id_cliente),
    fecha_pedido  DATE          NOT NULL,
    estado        VARCHAR2(20)  NOT NULL
);

CREATE TABLE pedido_lineas (
    id_pedido    NUMBER(10) NOT NULL REFERENCES pedidos (id_pedido),
    id_producto  NUMBER(10) NOT NULL REFERENCES productos (id_producto),
    cantidad     NUMBER(5)  NOT NULL,
    CONSTRAINT pk_pedido_lineas PRIMARY KEY (id_pedido, id_producto)
);

INSERT INTO clientes VALUES (1, 'Ana García',      'ana.garcia@ejemplo.com',      'Madrid');
INSERT INTO clientes VALUES (2, 'Luis Martínez',   'luis.martinez@ejemplo.com',   'Zaragoza');
INSERT INTO clientes VALUES (3, 'Marta López',     'marta.lopez@ejemplo.com',     'Sevilla');
INSERT INTO clientes VALUES (4, 'Carlos Ruiz',     'carlos.ruiz@ejemplo.com',     'Valencia');
INSERT INTO clientes VALUES (5, 'Lucía Fernández', 'lucia.fernandez@ejemplo.com', 'Bilbao');
INSERT INTO clientes VALUES (6, 'Pablo Sánchez',   'pablo.sanchez@ejemplo.com',   'Málaga');
INSERT INTO clientes VALUES (7, 'Elena Torres',    'elena.torres@ejemplo.com',    'Madrid');
INSERT INTO clientes VALUES (8, 'David Moreno',    'david.moreno@ejemplo.com',    'Granada');

INSERT INTO productos VALUES (101, 'Teclado mecánico',      'Periféricos',     79.90);
INSERT INTO productos VALUES (102, 'Ratón inalámbrico',     'Periféricos',     24.50);
INSERT INTO productos VALUES (103, 'Monitor 27" 4K',        'Pantallas',      329.00);
INSERT INTO productos VALUES (104, 'Auriculares Bluetooth', 'Audio',           59.99);
INSERT INTO productos VALUES (105, 'Webcam Full HD',        'Periféricos',     45.00);
INSERT INTO productos VALUES (106, 'Disco SSD 1 TB',        'Almacenamiento',  89.90);
INSERT INTO productos VALUES (107, 'Memoria RAM 16 GB',     'Componentes',     54.50);
INSERT INTO productos VALUES (108, 'Altavoces 2.1',         'Audio',           69.00);
INSERT INTO productos VALUES (109, 'Hub USB-C',             'Accesorios',      34.90);
INSERT INTO productos VALUES (110, 'Cable HDMI 2 m',        'Accesorios',       9.99);
INSERT INTO productos VALUES (111, 'Silla ergonómica',      'Mobiliario',     239.00);
INSERT INTO productos VALUES (112, 'Lámpara LED de mesa',   'Mobiliario',      28.00);

INSERT INTO pedidos VALUES (1001, 1, DATE '2026-01-08', 'Entregado');
INSERT INTO pedidos VALUES (1002, 2, DATE '2026-01-12', 'Entregado');
INSERT INTO pedidos VALUES (1003, 3, DATE '2026-01-19', 'Enviado');
INSERT INTO pedidos VALUES (1004, 1, DATE '2026-02-02', 'Entregado');
INSERT INTO pedidos VALUES (1005, 4, DATE '2026-02-07', 'Entregado');
INSERT INTO pedidos VALUES (1006, 5, DATE '2026-02-14', 'Cancelado');
INSERT INTO pedidos VALUES (1007, 6, DATE '2026-02-21', 'Enviado');
INSERT INTO pedidos VALUES (1008, 7, DATE '2026-03-03', 'Entregado');
INSERT INTO pedidos VALUES (1009, 2, DATE '2026-03-09', 'Entregado');
INSERT INTO pedidos VALUES (1010, 8, DATE '2026-03-16', 'Pendiente');
INSERT INTO pedidos VALUES (1011, 3, DATE '2026-03-23', 'Entregado');
INSERT INTO pedidos VALUES (1012, 4, DATE '2026-04-01', 'Enviado');
INSERT INTO pedidos VALUES (1013, 7, DATE '2026-04-08', 'Entregado');
INSERT INTO pedidos VALUES (1014, 5, DATE '2026-04-15', 'Pendiente');
INSERT INTO pedidos VALUES (1015, 6, DATE '2026-04-22', 'Entregado');

INSERT INTO pedido_lineas VALUES (1001, 101, 1);
INSERT INTO pedido_lineas VALUES (1001, 102, 1);
INSERT INTO pedido_lineas VALUES (1002, 103, 1);
INSERT INTO pedido_lineas VALUES (1003, 104, 2);
INSERT INTO pedido_lineas VALUES (1003, 110, 3);
INSERT INTO pedido_lineas VALUES (1004, 106, 1);
INSERT INTO pedido_lineas VALUES (1004, 107, 2);
INSERT INTO pedido_lineas VALUES (1005, 111, 1);
INSERT INTO pedido_lineas VALUES (1005, 112, 2);
INSERT INTO pedido_lineas VALUES (1006, 105, 1);
INSERT INTO pedido_lineas VALUES (1007, 108, 1);
INSERT INTO pedido_lineas VALUES (1007, 109, 1);
INSERT INTO pedido_lineas VALUES (1008, 101, 1);
INSERT INTO pedido_lineas VALUES (1008, 105, 1);
INSERT INTO pedido_lineas VALUES (1008, 110, 2);
INSERT INTO pedido_lineas VALUES (1009, 103, 1);
INSERT INTO pedido_lineas VALUES (1009, 102, 2);
INSERT INTO pedido_lineas VALUES (1010, 104, 1);
INSERT INTO pedido_lineas VALUES (1011, 106, 2);
INSERT INTO pedido_lineas VALUES (1011, 107, 1);
INSERT INTO pedido_lineas VALUES (1012, 111, 1);
INSERT INTO pedido_lineas VALUES (1013, 109, 2);
INSERT INTO pedido_lineas VALUES (1013, 110, 1);
INSERT INTO pedido_lineas VALUES (1014, 112, 3);
INSERT INTO pedido_lineas VALUES (1015, 102, 1);
INSERT INTO pedido_lineas VALUES (1015, 101, 1);
INSERT INTO pedido_lineas VALUES (1015, 104, 1);

COMMIT;
EXIT
