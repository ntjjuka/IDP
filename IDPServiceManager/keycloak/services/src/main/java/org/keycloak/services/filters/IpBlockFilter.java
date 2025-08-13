package org.keycloak.services.filters;

import jakarta.annotation.PostConstruct;
import jakarta.enterprise.context.ApplicationScoped;
import jakarta.ws.rs.Priorities;
import jakarta.ws.rs.container.ContainerRequestContext;
import jakarta.ws.rs.container.ContainerRequestFilter;
import jakarta.ws.rs.ext.Provider;

import org.jboss.logging.Logger;

import io.quarkus.arc.Unremovable;

import org.eclipse.microprofile.config.ConfigProvider;
import org.apache.commons.net.util.SubnetUtils;

import java.io.BufferedReader;
import java.io.IOException;
import java.net.InetAddress;
import java.nio.file.*;
import java.util.*;

@Provider
@ApplicationScoped
@Unremovable
@jakarta.annotation.Priority(Priorities.AUTHENTICATION)
public class IpBlockFilter implements ContainerRequestFilter {

    private static final Logger LOG = Logger.getLogger(IpBlockFilter.class);

    private String blockedIpsFile;

    private final Set<String> blockedIps = new HashSet<>();
    private final List<SubnetUtils> blockedCidrs = new ArrayList<>();

    @PostConstruct
    public void init() {
        try {
            blockedIpsFile = ConfigProvider.getConfig()
                    .getOptionalValue("blocked-ips-file", String.class)
                    .orElse("keycloak/conf/blocked-ips.conf");

            Path path = Path.of(blockedIpsFile);

            if (!path.isAbsolute()) {
                path = Paths.get(System.getProperty("user.dir")).resolve(path).normalize();
            }

            blockedIpsFile = path.toString();

            LOG.infof("Loading blocked IPs from: %s", blockedIpsFile);

            if (!Files.exists(path)) {
                LOG.errorf("Blocked IPs file does not exist: %s", blockedIpsFile);
            }

            loadBlockedIps();           
            watchBlockedIpsFile();      
        }catch(Exception e){
            LOG.errorf(e, "Failed to load blocked IPs file: %s", blockedIpsFile);
        }
    }


    private synchronized void loadBlockedIps() {
        blockedIps.clear();
        blockedCidrs.clear();

        Path path = Path.of(blockedIpsFile);

        try (BufferedReader reader = Files.newBufferedReader(path)) {
            reader.lines()
                .map(String::trim)
                .filter(line -> !line.isEmpty() && !line.startsWith("#"))
                .forEach(ip -> {
                    try {
                        if (ip.contains("/")) {
                            SubnetUtils subnet = new SubnetUtils(ip);
                            subnet.setInclusiveHostCount(true);
                            blockedCidrs.add(subnet);
                            LOG.infof("Blocked CIDR added: %s", ip);
                        } else {
                            InetAddress.getByName(ip);
                            blockedIps.add(ip);
                            LOG.infof("Blocked IP added: %s", ip);
                        }
                    } catch (Exception e) {
                        LOG.warnf("Invalid entry in blocked-ips.conf: '%s'. Reason: %s", ip, e.getMessage());
                    }
                });
        } catch (IOException e) {
            LOG.errorf(e, "Failed to load blocked IPs file: %s", blockedIpsFile);
        }

        LOG.infof("Blocked IPs loaded: %s", blockedIps);
        LOG.infof("Blocked CIDRs loaded: %s", blockedCidrs);
    }

    private void watchBlockedIpsFile() {
        Thread watcherThread = new Thread(() -> {
            try {
                WatchService watchService = FileSystems.getDefault().newWatchService();
                Path filePath = Path.of(blockedIpsFile);
                Path dir = filePath.getParent();
                Path fileName = filePath.getFileName();

                dir.register(watchService, StandardWatchEventKinds.ENTRY_MODIFY);

                LOG.infof("Started watching %s for changes", blockedIpsFile);

                while (true) {
                    WatchKey key = watchService.take();  

                    for (WatchEvent<?> event : key.pollEvents()) {
                        Path changed = (Path) event.context();
                        if (changed.equals(fileName)) {
                            LOG.infof("blocked-ips.conf changed. Reloading...");
                            loadBlockedIps();
                        }
                    }

                    boolean valid = key.reset();
                    if (!valid) {
                        break;
                    }
                }
            } catch (Exception e) {
                LOG.error("Failed to watch blocked-ips.conf", e);
            }
        });
        watcherThread.setDaemon(true);
        watcherThread.setName("BlockedIpsWatcher");
        watcherThread.start();
    }

    @Override
    public void filter(ContainerRequestContext requestContext) {
    try {
            String ip = getClientIp(requestContext);

            if (ip == null) {
                LOG.warn("Could not determine client IP, allowing request.");
                return;
            }

            if (isBlocked(ip)) {
                LOG.warnf("Blocked IP attempted access: %s", ip);
                requestContext.abortWith(
                    jakarta.ws.rs.core.Response.status(jakarta.ws.rs.core.Response.Status.FORBIDDEN)
                            .entity("Your IP is blocked.")
                            .build()
                );
            }

        } catch (Exception e) {
            LOG.errorf(e, "Failed to evaluate IP blocking logic. Allowing request.");
        }
    }

    private boolean isBlocked(String ip) {
        if (blockedIps.contains(ip)) {
            return true;
        }
        for (SubnetUtils cidr : blockedCidrs) {
            if (cidr.getInfo().isInRange(ip)) {
                return true;
            }
        }
        return false;
    }


    public static String getClientIp(ContainerRequestContext requestContext) {
        String ip = null;

        try {
            // 1. X-Forwarded-For 헤더 확인 (프록시 환경)
            String xForwardedForHeader = requestContext.getHeaderString("X-Forwarded-For");
            if (xForwardedForHeader != null && !xForwardedForHeader.isEmpty()) {
                ip = xForwardedForHeader.split(",")[0].trim(); // 첫 번째 값이 클라이언트 IP
                LOG.debugf("Client IP from X-Forwarded-For: %s", ip);
            }

            // 2. X-Real-IP 헤더 (일부 프록시 설정에서 사용)
            if (ip == null || ip.isEmpty()) {
                String xRealIp = requestContext.getHeaderString("X-Real-IP");
                if (xRealIp != null && !xRealIp.isEmpty()) {
                    ip = xRealIp.trim();
                    LOG.debugf("Client IP from X-Real-IP: %s", ip);
                }
            }

            // 3. RemoteAddr (직접 연결)
            if (ip == null || ip.isEmpty()) {
                ip = Optional.ofNullable(requestContext.getUriInfo())
                        .map(uriInfo -> uriInfo.getRequestUri().getHost())
                        .orElse("unknown");
                LOG.debugf("Client IP from RequestUri: %s", ip);
            }

        } catch (Exception e) {
            LOG.warn("Failed to determine client IP", e);
            ip = "unknown";
        }

        return ip;
    }
}
