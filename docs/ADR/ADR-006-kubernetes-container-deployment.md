# ADR-006: Kubernetes and Container-Based Deployment

## Status
Accepted

## Context

The migration from monolith to microservices requires a deployment platform that supports:

- **Multiple independently deployable services** (4-5 services in target architecture)
- **Service discovery and load balancing** (services need to find and communicate with each other)
- **Health monitoring and automatic recovery** (restart failed containers)
- **Rolling updates with zero downtime** (blue-green or canary deployments)
- **Resource management** (CPU, memory limits per service)
- **Scalability** (horizontal scaling based on load)
- **Observability** (logs, metrics, traces aggregation)
- **Cloud-agnostic** (avoid vendor lock-in, support both cloud and on-premises)

Current State:
- Monolith deployed as single IIS application or Azure App Service
- Manual deployment process
- No container usage
- Limited scalability (vertical scaling only)

Target State:
- 4-5 microservices (Product Catalog, Cart, Checkout, Order, Web BFF)
- Automated deployment pipeline
- Independent scaling per service
- Self-healing and high availability

## Decision

We will adopt **Kubernetes** as the container orchestration platform with **Docker** as the container runtime.

### Deployment Platform

**Primary**: **Azure Kubernetes Service (AKS)** (managed Kubernetes on Azure)

**Alternative**: On-premises Kubernetes (using kubeadm, Rancher, or OpenShift) if cloud not available

### Technology Stack

| Component | Technology | Version | Purpose |
|-----------|------------|---------|---------|
| **Container Runtime** | Docker | 24.0+ | Build and package services as images |
| **Orchestration** | Kubernetes | 1.28+ | Service deployment, scaling, management |
| **Ingress Controller** | NGINX Ingress | 1.9+ | HTTP routing, TLS termination |
| **Service Mesh** (future) | Istio or Linkerd | - | mTLS, advanced traffic management |
| **Registry** | Azure Container Registry (ACR) | - | Private Docker image storage |

### Rationale for Kubernetes

✅ **Industry Standard**
- De-facto standard for container orchestration (90%+ market share)
- Large ecosystem (Helm charts, operators, tooling)
- Strong community support and documentation
- Hiring: Kubernetes skills widely available

✅ **Cloud-Agnostic**
- Runs on Azure (AKS), AWS (EKS), GCP (GKE), on-premises
- Avoids vendor lock-in (can migrate between clouds)
- Standard API across providers

✅ **Built-in Features**
- Service discovery via DNS (e.g., `http://cart-service.default.svc.cluster.local`)
- Load balancing (traffic distributed across pod replicas)
- Rolling updates and rollbacks (`kubectl rollout undo`)
- Health checks (liveness, readiness, startup probes)
- Secrets and config management (ConfigMaps, Secrets)
- Resource limits and requests (CPU, memory per pod)

✅ **Scalability**
- Horizontal Pod Autoscaler (HPA) - scale based on CPU, memory, or custom metrics
- Cluster Autoscaler - add/remove nodes based on demand
- Supports 1000+ nodes, 100,000+ pods per cluster (well beyond our needs)

✅ **.NET Support**
- Excellent Docker support for .NET 8 (official Microsoft images)
- Kubernetes client libraries for .NET (KubernetesClient, Steeltoe)
- Microsoft documentation for AKS + .NET

### Rationale for Azure Kubernetes Service (AKS)

✅ **Managed Control Plane**
- Microsoft manages Kubernetes control plane (API server, scheduler, controller manager)
- Free control plane (only pay for worker nodes)
- Automatic upgrades and patching

✅ **Azure Integration**
- Native integration with Azure Container Registry (ACR) - no credentials needed
- Azure AD integration for RBAC (role-based access control)
- Azure Monitor for container insights (logs, metrics, traces)
- Virtual Network integration (private cluster option)
- Azure Key Vault for secrets management (CSI driver)

✅ **Enterprise Features**
- SLA: 99.95% uptime (with Availability Zones)
- Node auto-repair (unhealthy nodes automatically replaced)
- Cluster autoscaler (scale nodes based on pod demand)
- Windows node pools (if needed, though we're using Linux)

✅ **Cost Optimization**
- Free control plane (save ~$70/month vs. self-managed)
- Azure Spot VMs for non-critical workloads (up to 90% discount)
- Reserved instances for predictable workloads (save 30-50%)

### Container Strategy

**Base Images**:
- **Runtime**: `mcr.microsoft.com/dotnet/aspnet:8.0` (145 MB, includes ASP.NET Core runtime)
- **Build**: `mcr.microsoft.com/dotnet/sdk:8.0` (700 MB, includes .NET SDK)

**Multi-Stage Dockerfile Pattern**:
```dockerfile
# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["Service.csproj", "./"]
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app/publish

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Service.dll"]
```

**Benefits**:
- Small final image (~150-200 MB vs. 700+ MB)
- Secure (no build tools in production image)
- Fast deployment (less data to transfer)

**Image Tagging Strategy**:
```
# Development
product-catalog-service:latest

# Production
product-catalog-service:v1.2.3        # Semantic versioning
product-catalog-service:git-abc123     # Git commit SHA
product-catalog-service:2025-01-19     # Date-based
```

### Deployment Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    Azure Kubernetes Service                  │
│                                                               │
│  ┌────────────────────────────────────────────────────────┐ │
│  │                   Ingress Controller                    │ │
│  │                    (NGINX Ingress)                      │ │
│  │          https://retail.example.com → Services          │ │
│  └───────────────────────┬────────────────────────────────┘ │
│                          │                                   │
│    ┌─────────────────────┼─────────────────────┐            │
│    │         Namespace: default                 │            │
│    │                                             │            │
│    │  ┌──────────────┐  ┌──────────────┐       │            │
│    │  │   Web BFF    │  │   Product    │       │            │
│    │  │  (3 pods)    │  │   Catalog    │       │            │
│    │  │              │  │  (3 pods)    │       │            │
│    │  └──────────────┘  └──────────────┘       │            │
│    │                                             │            │
│    │  ┌──────────────┐  ┌──────────────┐       │            │
│    │  │   Cart Svc   │  │  Checkout    │       │            │
│    │  │  (3 pods)    │  │    Service   │       │            │
│    │  │              │  │  (2 pods)    │       │            │
│    │  └──────────────┘  └──────────────┘       │            │
│    │                                             │            │
│    │  ┌──────────────┐                          │            │
│    │  │  Order Svc   │                          │            │
│    │  │  (2 pods)    │                          │            │
│    │  └──────────────┘                          │            │
│    └─────────────────────────────────────────────┘            │
│                                                               │
│  ┌────────────────────────────────────────────────────────┐ │
│  │              Observability Stack                       │ │
│  │  Prometheus │ Grafana │ Jaeger │ Fluent Bit           │ │
│  └────────────────────────────────────────────────────────┘ │
└───────────────────────────────────────────────────────────────┘
         │                                     │
         ▼                                     ▼
┌──────────────────┐              ┌──────────────────────┐
│  Azure Container │              │   Azure SQL Database │
│     Registry     │              │  (Managed Database)  │
└──────────────────┘              └──────────────────────┘
```

### Resource Allocation

**Per Service** (initial sizing, adjust based on metrics):

| Service | Replicas | CPU Request | CPU Limit | Memory Request | Memory Limit |
|---------|----------|-------------|-----------|----------------|--------------|
| **Web BFF** | 3 | 200m | 1000m | 256Mi | 512Mi |
| **Product Catalog** | 3 | 100m | 500m | 128Mi | 256Mi |
| **Cart Service** | 3 | 100m | 500m | 128Mi | 256Mi |
| **Order Service** | 2 | 100m | 500m | 128Mi | 256Mi |
| **Checkout Service** | 2 | 200m | 1000m | 256Mi | 512Mi |

**Node Pool** (AKS):
- Node VM Size: `Standard_D4s_v3` (4 vCPU, 16 GB RAM)
- Initial Node Count: 3 nodes (for high availability)
- Max Node Count: 10 nodes (cluster autoscaler)
- Total Capacity: 12 vCPU, 48 GB RAM (can run ~20-30 pods)

**Justification**:
- Requests ensure minimum resources guaranteed
- Limits prevent runaway processes from affecting other pods
- Multiple replicas provide high availability
- Autoscaling allows handling traffic spikes

### Deployment Strategy

**Rolling Update** (default for Deployments):
```yaml
strategy:
  type: RollingUpdate
  rollingUpdate:
    maxSurge: 1         # Max 1 extra pod during update
    maxUnavailable: 0   # Ensure no downtime
```

**Deployment Process**:
1. New version deployed alongside old version
2. Kubernetes waits for health checks to pass
3. Old version terminated only after new version healthy
4. Repeat until all pods updated

**Blue-Green Deployment** (for risky changes):
- Deploy new version as separate Deployment
- Test new version via internal URL
- Switch Service selector to new version
- Keep old version for 24 hours (rollback window)

**Canary Deployment** (gradual rollout):
- Use Ingress annotations to split traffic:
  - 10% → new version (canary)
  - 90% → old version (stable)
- Monitor metrics (error rate, latency)
- Gradually increase canary percentage (10% → 50% → 100%)

**Rollback**:
```bash
# Instant rollback to previous version
kubectl rollout undo deployment/cart-service

# Rollback to specific version
kubectl rollout undo deployment/cart-service --to-revision=3

# Check rollout status
kubectl rollout status deployment/cart-service
```

### Service Discovery and Communication

**Kubernetes DNS** (built-in):
- Each Service gets DNS name: `<service-name>.<namespace>.svc.cluster.local`
- Example: `http://cart-service.default.svc.cluster.local:8082`
- Short form (same namespace): `http://cart-service:8082`

**Service Types**:
- **ClusterIP** (internal services): Product Catalog, Cart, Checkout, Order
- **LoadBalancer** (external services): None directly (use Ingress instead)

**Configuration** (in appsettings.json or environment variables):
```json
{
  "CartServiceUrl": "http://cart-service:8082",
  "ProductCatalogUrl": "http://product-catalog-service:8081"
}
```

### Health Checks

**Three Types**:

1. **Liveness Probe** (is the process running?)
   ```yaml
   livenessProbe:
     httpGet:
       path: /health/live
       port: 8080
     initialDelaySeconds: 30
     periodSeconds: 10
   ```
   - If fails: Kubernetes restarts pod

2. **Readiness Probe** (is the service ready for traffic?)
   ```yaml
   readinessProbe:
     httpGet:
       path: /health/ready
       port: 8080
     initialDelaySeconds: 10
     periodSeconds: 5
   ```
   - If fails: Pod removed from Service load balancer

3. **Startup Probe** (has initialization completed?)
   ```yaml
   startupProbe:
     httpGet:
       path: /health/startup
       port: 8080
     failureThreshold: 30
     periodSeconds: 10
   ```
   - Allows slow startup (e.g., database migrations)

**Implementation** (in each service):
```csharp
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false // No checks, just "alive"
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Name == "database"
});
```

### Configuration Management

**ConfigMaps** (non-sensitive configuration):
```yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: cart-service-config
data:
  CartExpiryHours: "72"
  LogLevel: "Information"
```

**Secrets** (sensitive data):
```yaml
apiVersion: v1
kind: Secret
metadata:
  name: database-secret
type: Opaque
data:
  connection-string: <base64-encoded>
```

**Azure Key Vault Integration** (preferred for production):
- Use CSI Secret Store Driver
- Secrets fetched from Key Vault at pod startup
- No secrets stored in Kubernetes etcd

**Environment Variables** (injected into pods):
```yaml
env:
- name: ConnectionStrings__DefaultConnection
  valueFrom:
    secretKeyRef:
      name: database-secret
      key: connection-string
- name: CartExpiryHours
  valueFrom:
    configMapKeyRef:
      name: cart-service-config
      key: CartExpiryHours
```

### Observability

**Logging**:
- Stdout/stderr captured by Kubernetes
- Aggregated by Fluent Bit or Azure Monitor
- Structured logging (JSON) for parsing

**Metrics**:
- Prometheus scrapes `/metrics` endpoint from each pod
- Grafana dashboards visualize metrics
- Azure Monitor integration (if using AKS)

**Tracing**:
- OpenTelemetry SDK in each service
- Traces sent to Jaeger or Azure Application Insights
- Correlation IDs propagate across service calls

## Consequences

### Positive

✅ **Independent Deployability**
- Deploy Product Catalog Service without touching Cart Service
- Reduces deployment risk and coordination overhead

✅ **Scalability**
- Scale Cart Service to 10 replicas during peak (Black Friday)
- Scale Product Catalog independently (high read traffic)

✅ **Resilience**
- If Cart Service crashes, Kubernetes restarts it automatically
- If node fails, pods rescheduled on healthy nodes
- Multiple replicas ensure availability during updates

✅ **Resource Efficiency**
- CPU/memory limits prevent resource starvation
- Bin-packing: Multiple small services on same node
- Autoscaling adds nodes only when needed

✅ **Developer Productivity**
- Same deployment mechanism for all services (learn once)
- Local development with Minikube or Docker Desktop
- Fast feedback loop (build, push, deploy in minutes)

✅ **Operational Excellence**
- Declarative configuration (GitOps-friendly)
- Rollback in seconds (`kubectl rollout undo`)
- Self-healing (unhealthy pods automatically replaced)

### Negative

⚠️ **Operational Complexity**
- Kubernetes steep learning curve (Pods, Services, Deployments, Ingress, ConfigMaps, Secrets)
- Networking complexity (Pod IP changes, service discovery, network policies)
- Troubleshooting harder (logs across multiple pods, distributed tracing required)

⚠️ **Infrastructure Cost**
- AKS control plane free, but worker nodes cost ~$200-500/month (3 nodes)
- Container registry storage cost (~$5-20/month)
- Load balancer cost (~$20/month)
- Total: ~$250-550/month (vs. $100-200 for single Azure App Service)

⚠️ **Initial Setup Time**
- Kubernetes cluster setup: 1-2 days
- CI/CD pipeline configuration: 2-3 days
- Monitoring stack setup: 1-2 days
- Total: 1 week before first service deployed

⚠️ **Local Development**
- Developers need Docker and Minikube/Kind installed
- More complex than `dotnet run` (build image, deploy to local cluster)
- Debugging requires port forwarding or telepresence

⚠️ **Security Surface**
- More attack vectors (Kubernetes API, container registry, network between pods)
- Need network policies, RBAC, pod security policies
- Secret management more complex (avoid secrets in images)

### Risks and Mitigations

| Risk | Mitigation |
|------|------------|
| **Kubernetes version skew** (AKS upgrades) | Subscribe to AKS release notes, test upgrades in staging |
| **Pod eviction** (node pressure) | Set resource requests/limits, use PodDisruptionBudgets |
| **Image pull failures** (ACR down) | Use imagePullPolicy: IfNotPresent, cache images on nodes |
| **Configuration drift** (manual kubectl changes) | Use GitOps (ArgoCD/Flux), deny manual changes via RBAC |
| **Cost overrun** (too many nodes) | Set node autoscaler limits, monitor costs in Azure Cost Management |

## Alternatives Considered

### 1. Azure App Service (PaaS)

**Pros**:
- Simpler (no Kubernetes knowledge required)
- Lower operational overhead
- Built-in autoscaling

**Cons**:
- ❌ Less flexible (limited to HTTP workloads)
- ❌ Harder to do service-to-service communication
- ❌ More expensive at scale (per-app pricing)
- ❌ Vendor lock-in (Azure-specific)

**Decision**: Rejected due to limited flexibility and vendor lock-in

### 2. Docker Compose (VM-based)

**Pros**:
- Simple (docker-compose.yml)
- Easy local development
- Lightweight

**Cons**:
- ❌ No orchestration (manual service discovery, no autoscaling)
- ❌ Single-node only (no high availability)
- ❌ Manual health checks and restarts
- ❌ Not production-ready

**Decision**: Use for local development only, not production

### 3. AWS ECS/Fargate

**Pros**:
- Serverless containers (no node management)
- AWS integration (IAM, CloudWatch, ALB)
- Pay-per-task (cost-effective for low traffic)

**Cons**:
- ❌ AWS-specific (vendor lock-in)
- ❌ Less ecosystem support than Kubernetes
- ❌ Team unfamiliar with AWS

**Decision**: Rejected due to vendor lock-in and team unfamiliarity

### 4. Nomad (HashiCorp)

**Pros**:
- Simpler than Kubernetes
- Supports non-container workloads (VMs, Java JARs)
- Lightweight

**Cons**:
- ❌ Smaller ecosystem (fewer tools, less community)
- ❌ Less industry adoption (hiring harder)
- ❌ No managed service (self-managed only)

**Decision**: Rejected due to smaller ecosystem and hiring concerns

## Implementation Plan

**Phase 0: Foundation** (Week 1-2)
1. Provision AKS cluster (`az aks create`)
2. Install NGINX Ingress Controller
3. Install Prometheus + Grafana (Helm charts)
4. Configure Azure Container Registry (ACR)
5. Test deployment (hello-world container)

**Phase 1: Monolith Migration** (Week 3-4)
1. Containerize existing monolith (Dockerfile)
2. Deploy monolith to AKS (3 replicas)
3. Configure Ingress (https://retail.example.com → monolith)
4. Validate health checks
5. Baseline metrics

**Phase 2: First Microservice** (Week 5-8)
1. Deploy Product Catalog Service to AKS
2. Update Web BFF to call Product Catalog Service
3. Canary deployment (10% → 100%)
4. Remove product logic from monolith

**Phase N: Remaining Services** (Week 9+)
1. Repeat for Cart, Checkout, Order services

## Assumptions

- **Azure subscription available** with sufficient quota (vCPUs, IPs)
- **Team has basic Linux knowledge** (Kubernetes uses Linux containers)
- **Network connectivity** between AKS and Azure SQL (private endpoint or firewall rules)
- **CI/CD pipeline** will be set up (GitHub Actions, Azure DevOps, or Jenkins)

## Related Decisions

- **ADR-005**: Strangler Fig Pattern - How Kubernetes enables incremental migration
- **ADR-007**: Service Boundaries - What services get deployed to Kubernetes
- **ADR-010**: CI/CD Pipeline - How services are built and deployed to Kubernetes
- **ADR-011**: Observability Stack - How Kubernetes integrates with monitoring

## Review and Approval

**Reviewed by**: DevOps Team, Architecture Team
**Approved by**: CTO, VP Engineering
**Date**: 2025-01-19

**Decision**: Proceed with Kubernetes (AKS) for container orchestration
