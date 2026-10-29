-- Check for postgresql-001 (marked answer: b).
-- run-checks runs the question's snippet unchanged in a fresh PostgreSQL 16 database and
-- compares the output with the expected block. The final query must print not_in = 0 and
-- not_exists = 2, which only option b ("0 and 2") states.

-- --- snippet ---
CREATE TABLE customers (id int PRIMARY KEY);
CREATE TABLE orders (
    id int PRIMARY KEY,
    customer_id int REFERENCES customers
);

INSERT INTO customers VALUES (1), (2), (3);
INSERT INTO orders VALUES (10, 1), (11, NULL); -- 11 is a guest checkout

SELECT
    (SELECT count(*) FROM customers c
     WHERE c.id NOT IN (SELECT customer_id FROM orders)) AS not_in,
    (SELECT count(*) FROM customers c
     WHERE NOT EXISTS (
         SELECT 1 FROM orders o WHERE o.customer_id = c.id)) AS not_exists;
-- --- end snippet ---

-- --- expected ---
-- not_in|not_exists
-- 0|2
-- --- end expected ---
