
![Comelit](https://pro.comelitgroup.com/images/logo.svg)

# Comelit Api Gateway
.NET 10 api gateway is a project aimed at integrating **Comelit Vedo** alarm system to **Home Assistant** (or what you want 🙂) with ease.

### Features
* Get global alarm status
* Get alarm status of single area
* Insert/remove alarm from a specific area
* Include/exclude elements (e.g. window) from a specific area
* Get radar status 

### What you can do with this project
* Arm/Disarm alarm system
* Get status of your alarm system
* Get status of areas
* Include/exclude elements from a specific area
* Get information from radar/motion sensors

### Installation steps

1. Pull docker image whith this command (available on [Docker Hub](https://raw.githubusercontent.com/Asganafer/comelit-api-gateway/e85dcaa167111795297a7ff18cc93ef3db384656/docs/images/comelit-logo.svg)):
~~~
docker pull giogdev/comelit-api-gateway:latest
~~~
2. Run container with this command:
~~~
docker run -d giogdev/comelit-api-gateway:latest
    -p 5000:5000
    -e ASPNETCORE_URLS=http://0.0.0.0:5000
    -e VEDO_KEY=<key>
    -e VEDO_URL=<url>
    -e VEDO_EXCLUDED_AREAS_ID=<id>
    -e ENABLE_SWAGGER=<true/false>
    -e KEEPALIVE_ENABLED=<true/false>
    -e KEEPALIVE_INTERVAL_SECONDS=<seconds>
~~~
or use docker-compose:
~~~
services:
  comelit-api-gateway:
    image: giogdev/comelit-api-gateway:latest
    ports:
      - "5000:5000"         
    environment:
      VEDO_KEY: "000000"
      VEDO_URL: "http://192.168.1.10"
      ENABLE_SWAGGER: "true"
      VEDO_EXCLUDED_AREAS_ID: "5,6,7"
      KEEPALIVE_ENABLED: "true"
      KEEPALIVE_INTERVAL_SECONDS: "60"
~~~


### Container parameters

##### VEDO_KEY [<span style="color:red">required</span>]
Code to lock and unlock your alarm system, the same used on your keypad.\
If you insert a wrong code your alarm will start ringing! \
_Example: 123456_
##### VEDO_URL [<span style="color:red">required</span>]
Local IP address of your vedo alarm system. \
_Example: http://192.168.1.10_
##### VEDO_EXCLUDED_AREAS_ID
List of area's IDs that are not configured in your system or you won't include in this gateway. \ 
By default there are 8 areas in comelit configuration but not all areas need to be configured.\
Separate them with comma.\
_Example: 5,6,7_
##### ENABLE_SWAGGER
Enable swagger UI, navigate _\<container_IP>\<container:port>/swagger_. \
Default = true \
_Example: true_
##### KEEPALIVE_ENABLED
Enable the background keep-alive ping that periodically queries the Comelit Vedo system to avoid session timeout on the CGI interface. \
**Required from version 2.15.X onwards**: without it the Comelit control unit goes into sleep mode. \
Default = false \
_Example: true_
##### KEEPALIVE_INTERVAL_SECONDS
Interval, in seconds, between keep-alive pings. Only relevant when _KEEPALIVE_ENABLED_ is true. \
Default = 60 \
_Example: 120_

## Home assistant integration
![Home assistant integration](https://raw.githubusercontent.com/Asganafer/comelit-api-gateway/refs/heads/main/docs/images/home-assistant-schema.jpg)

You can call the APIs of Comelit Api Gateway by adding them as alarm, switch and/or rest command. 
Here you can see how to configure them as **switch commands**.

1. Open _configuration.yaml_ in Home Assistant directory
1. Add a new line:
~~~yaml
switch: !include switch.yaml
~~~
3. In the same directory create new file "_switch.yaml_"
1. Here you can add api calls, for example:
~~~yaml
#Switch configuration for total alarm
- platform: rest
  name: switch total alarm system 
  unique_id: alarm_total_switch
  resource: http://<container_ip>:<container_port>/vedo/areas/all/arm-disarm
  state_resource: http://<container_ip>:<container_port>/vedo/areas/all/is-active
  method: POST
  verify_ssl: false
  is_on_template: "{{ value_json }}"

#Switch configuration for garage
- platform: rest
  name: switch garage
  unique_id: alarm_switch_garage
  resource: http://<container_ip>:<container_port>/vedo/areas/<area_id>/arm-disarm
  state_resource: http://<container_ip>:<container_port>/vedo/areas/<area_id>/is-active
  method: POST
  verify_ssl: false
  is_on_template: "{{ value_json }}"
~~~
_<area_id>_ you can discover the areas configurated in your alarm system with this API: <span style="color:blue">_http://<container_ip>:<container_port>/Vedo/areas_</span>

5. Open Home Assistant (on browser or app)
1. Go to **Home--> Developer tools**
1. Reboot Home Assistant
1. Now you can add a switch to your Home Assistant UI:

![Home assistant switch](https://raw.githubusercontent.com/Asganafer/comelit-api-gateway/refs/heads/main/docs/images/home-assistant-switch.png)


## Contribution
This project is distributed under the MIT license. See the [LICENSE](LICENSE) file for more information. \
**Text me** if you want to partecipate to this project!