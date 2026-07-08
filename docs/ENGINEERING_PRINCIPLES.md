# H1Stats Engineering Principles

These principles govern all architectural and implementation decisions for H1Stats.

1. **Prioritize maintainability over speed.** Code should be easy to understand and extend years from now.
2. **Separate business logic, data access, and presentation layers.** Controllers and UI components stay thin.
3. **Use dependency injection throughout.** All services are registered and resolved via DI.
4. **Design for modular expansion.** New modules plug in without modifying the core application.
5. **Avoid hard-coded SQL in controllers.** Queries live in repositories; configuration drives report metadata.
6. **Build reusable components and widgets.** Dashboard widgets are shared across surfaces (dashboard, reports, TV displays).
7. **Document public APIs and major classes.** XML docs on public interfaces; README for setup and module registration.
8. **Prefer configuration over code where practical.** Database connections, module permissions, and report metadata are configurable.
9. **Keep the UI clean, fast, and consistent.** Hearts 1st branding; target &lt; 3 second response times.
10. **Assume H1Stats will become the primary operational platform for Hearts 1st over the next decade.**
