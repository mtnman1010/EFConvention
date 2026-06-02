# AI Implementation Assistant

This page helps you generate a tailored prompt for an AI assistant (Claude, ChatGPT, Copilot etc.) to scaffold an EFConventionBuilder implementation for your specific project. Answer the questions below, then copy the filled-in prompt at the bottom and paste it into your AI assistant of choice.

---

## Questions to answer before generating your prompt

Work through each section. Your answers replace the `[TOKEN]` placeholders in the prompt at the bottom.

---

### 1. Project type

What kind of application are you building?

- [ ] ASP.NET Core Web API
- [ ] ASP.NET Core MVC / Razor Pages
- [ ] Blazor Server
- [ ] WPF / WinForms desktop app
- [ ] Console / CLI tool
- [ ] Background job / worker service
- [ ] Class library (no UI)

**Your answer:** `[PROJECT_TYPE]`

---

### 2. Database

What database are you targeting?

- [ ] SQL Server
- [ ] PostgreSQL
- [ ] SQLite
- [ ] Other: ___________

**Your answer:** `[DATABASE]`

---

### 3. Naming convention

Which naming convention do you want?

- [ ] PascalCase (default — recommended for SQL Server)
- [ ] snake_case (recommended for PostgreSQL)
- [ ] Pluralized tables + PascalCase columns
- [ ] Custom (describe below)

**Your answer:** `[NAMING_CONVENTION]`

---

### 4. Audit stamping

Do you need audit fields (`CreatedDate`, `CreatedBy`, `ModifiedDate`, `ModifiedBy`)?

- [ ] Yes — full audit on all entities
- [ ] Yes — only on some entities (list them below)
- [ ] No

If yes, which entities need audit stamping? `[AUDITABLE_ENTITIES]`

---

### 5. Soft delete

Do you need soft delete (`IsDeleted`, `DeletedDate`, `DeletedBy`)?

- [ ] Yes — soft delete on all entities
- [ ] Yes — only on some entities (list them below)
- [ ] No — hard delete only

If yes, which entities need soft delete? `[SOFT_DELETE_ENTITIES]`

---

### 6. Domain entities

List your domain entities. For each, note:
- What it represents
- Its key relationships (one-to-many, optional FK etc.)
- Whether it needs audit / soft delete

Example:
```
Customer — a store customer
  - has many Orders (one-to-many)
  - has one Address (required FK)
  - IAuditable, hard delete

Order — a customer order
  - belongs to Customer (required)
  - has many OrderItems (one-to-many)
  - IAuditable + ISoftDelete
```

**Your entities:** `[DOMAIN_ENTITIES]`

---

### 7. Identity / current user

How does your application know who the current user is?

- [ ] ASP.NET Core — HTTP context claims principal
- [ ] JWT — claims from Bearer token
- [ ] Blazor Server — AuthenticationStateProvider
- [ ] Windows authentication — WindowsIdentity
- [ ] Custom login session — describe: ___________
- [ ] Background job — always "system"
- [ ] No audit stamping — not needed

**Your answer:** `[IDENTITY_SOURCE]`

---

### 8. Services needed

List the application services you need. For each, describe the key operations:

Example:
```
IOrderService — GetOrder, GetOrdersByCustomer, PlaceOrder, DeleteOrder, RestoreOrder
ICustomerService — GetCustomer, AddCustomer, UpdateCustomer, DeleteCustomer
IProductService — GetProduct, SearchProducts, AddProduct, DeleteProduct
```

**Your services:** `[SERVICES]`

---

### 9. DI framework

How are you registering services?

- [ ] ASP.NET Core built-in DI (`IServiceCollection`)
- [ ] .NET Generic Host (`IServiceCollection`)
- [ ] Manual / no DI container

**Your answer:** `[DI_FRAMEWORK]`

---

### 10. Testing

What testing approach do you want?

- [ ] Unit tests with Moq (no database)
- [ ] Integration tests with EF Core in-memory provider
- [ ] Both
- [ ] None for now

**Your answer:** `[TESTING_APPROACH]`

---

### 11. Additional requirements

Any other requirements or constraints?

- Legacy schema with existing column names to map to?
- Multiple databases or bounded contexts?
- Specific EF Core features (raw SQL, owned entities, value objects)?
- Specific .NET version?

**Your answer:** `[ADDITIONAL_REQUIREMENTS]`

---

## The prompt

Once you have answered the questions above, copy the prompt below, replace every `[TOKEN]` with your answer, and paste it into your AI assistant.

---

```
I am building a [PROJECT_TYPE] using EFConventionBuilder — a convention-over-configuration 
library for EF Core. Please scaffold a complete implementation for my project.

## Database
Target database: [DATABASE]
Naming convention: [NAMING_CONVENTION]

## Domain entities
[DOMAIN_ENTITIES]

## Behaviour
Audit stamping (IAuditable): [AUDITABLE_ENTITIES]
Soft delete (ISoftDelete): [SOFT_DELETE_ENTITIES]

## Identity
Current user resolved from: [IDENTITY_SOURCE]

## Services required
[SERVICES]

## DI registration
Using: [DI_FRAMEWORK]

## Testing
[TESTING_APPROACH]

## Additional requirements
[ADDITIONAL_REQUIREMENTS]

---

Please generate the following files:

1. **Domain entities** — C# classes implementing IEntity, IAuditable, and/or ISoftDelete 
   as appropriate. Use nullable reference type annotations for required/optional FK 
   relationships (non-nullable navigation = required, nullable navigation = optional). 
   No scalar FK properties unless specifically needed. Collection navigation properties 
   must use private set;

2. **ICurrentUserService implementation** — appropriate for [IDENTITY_SOURCE].

3. **UnitOfWork subclass** — concrete DbContext using the correct naming convention 
   and WithFullAudit() / WithSoftDelete() / no audit as appropriate. Assembly anchor 
   should be any domain entity type.

4. **Application service interfaces and implementations** — one per service listed above, 
   inheriting ServiceBase<TEntity> where appropriate. Use UnitOfWork.Query<T>() with 
   explicit Include() calls. Guard optional FK navigations with null checks before 
   accessing properties in LINQ expressions.

5. **DI registration** — complete registration in [DI_FRAMEWORK] for ICurrentUserService, 
   IUnitOfWork, and all application services.

6. **[TESTING_APPROACH]** — test infrastructure (FixedUserService, InMemoryDb) and 
   example tests covering add, soft delete, restore, and any business logic validation.

Please follow these conventions throughout:
- FK columns named after navigation property (Customer not CustomerId)
- Collection properties use private set;
- Audit properties: CreatedDate, CreatedBy, ModifiedDate, ModifiedBy
- Soft delete properties: IsDeleted, DeletedDate, DeletedBy
- All async methods use CancellationToken ct = default
- Services throw KeyNotFoundException when entity not found
- Nullable reference types enabled (<Nullable>enable</Nullable>)
```

---

## Example — filled in

Here is a complete example using the Store domain from the EFConventionBuilder sample project:

```
I am building an ASP.NET Core Web API using EFConventionBuilder — a convention-over-configuration 
library for EF Core. Please scaffold a complete implementation for my project.

## Database
Target database: SQL Server
Naming convention: PascalCase (default)

## Domain entities
Address — a physical mailing address
  - plain entity, no audit, no soft delete

Category — a product category
  - plain entity
  - has many Products

Customer — a store customer
  - has one Address (required)
  - has many Orders
  - has many ProductReviews (optional FK back to Customer)
  - IAuditable, hard delete

Product — a store product
  - has one Category (required)
  - has many OrderItems
  - Price and CostPrice use [Precision(18,2)]
  - IAuditable + ISoftDelete

Order — a customer order
  - belongs to Customer (required)
  - has many OrderItems
  - TotalAmount uses [Precision(18,2)]
  - IAuditable + ISoftDelete

OrderItem — a line item within an Order
  - belongs to Order (required)
  - belongs to Product (required)
  - UnitPrice uses [Precision(18,2)]
  - plain entity

ProductReview — a customer review for a product
  - belongs to Product (required)
  - belongs to Customer (optional — review survives customer deletion)
  - IAuditable

## Behaviour
Audit stamping: Customer, Product, Order, ProductReview
Soft delete: Product, Order

## Identity
Current user resolved from: ASP.NET Core HTTP context claims principal

## Services required
ICustomerService — GetCustomer, AddCustomer, UpdateCustomer, DeleteCustomer
IProductService — GetProduct, SearchProducts, AddProduct, DeleteProduct, RestoreProduct
IOrderService — GetOrder, GetOrdersByCustomer, PlaceOrder, DeleteOrder, RestoreOrder, PurgeOrder
IProductReviewService — GetReviewsForProduct, GetReviewsByCustomer, AddReview, DeleteReview

## DI registration
Using: ASP.NET Core built-in DI (IServiceCollection) in Program.cs

## Testing
Both unit tests with Moq and integration tests with EF Core in-memory provider

## Additional requirements
None
```

---

## Tips for better results

**Be specific about relationships** — "has many Orders" is good, "has many Orders (a customer can have zero or more orders, orders are soft-deleted)" is better. The more context you give, the more accurate the generated code will be.

**List decimal properties explicitly** — mention which properties need `[Precision(18,2)]` — prices, amounts, rates etc. The AI will add the attribute automatically if you call it out.

**Describe optional FKs clearly** — "ProductReview has an optional Customer (review survives customer deletion)" gives the AI the context to use `Customer?` (nullable) rather than `Customer` (required).

**Mention legacy schema constraints** — if you have an existing database with column names that differ from the defaults, describe them: "the audit columns are named RecordCreatedDate and RecordCreatedUser". The AI will generate the appropriate `WithAuditFields` override.

**Iterate** — use the generated code as a starting point. Ask follow-up questions: "Add a GetDeletedProductsAsync method to IProductService" or "Update OrderService to validate that all products in an order are active before placing it".
