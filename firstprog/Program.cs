namespace firstprog;
using System;
using System.Device.Gpio;
using System.Device.I2c;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using Iot.Device.CharacterLcd;
using Iot.Device.Pcx857x;
using Iot.Device.Bmxx80;
using Iot.Device.Bmxx80.PowerMode;
using Newtonsoft.Json;
using Microsoft.Azure.Devices.Client;
using Azure;
using Azure.Communication.Email;

class Program
{
    private static DeviceClient s_deviceClient;  
    private readonly static string s_connectionString01 = "HostName=raspHelpMe.azure-devices.net;DeviceId=raspdev;SharedAccessKey=wxN/1pd09jXWTxC37swSZ4YeWuq+mTCsPAIoTDNHUPU="; 
    
    static async Task sendHTTP(double temperature, double humidity, bool hvacOn) {

        int hvac=0;

        if(hvacOn){
            hvac=1;
        }

        string functionUrl = "https://hvacmeplease.azurewebsites.net/api/HttpTrigger2?temperature=" + (int)temperature + "&humidity=" + (int)humidity ; //"https://hvac-function.azurewebsites.net/api/Http-hvac";
        
        using (HttpClient client = new HttpClient())
        {
            
             var data = new {

                Name = "John Doe",
                Action = "Purchase",
                Amount = 100.00
            };

            var json = JsonConvert.SerializeObject(data);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            HttpResponseMessage response = await client.PostAsync(functionUrl, content);


             if (response.IsSuccessStatusCode) {
                Console.WriteLine("Webhook sent successfully.");
                string result =await response.Content.ReadAsStringAsync();
                Console.WriteLine(result);
                bool switc=result=="1";
                if(result=="1"){
                    if(switc){
                        Hvacdisplay(true);
                       // switc=!switc;
                    }
                }
                else{
                    if(!switc){
                        Hvacdisplay(false);
                       // switc=!switc;
                    }
                }
             }
             else
             {
                 Console.WriteLine($"Failed to send webhook. Status code: {response.StatusCode}");
             }
            //var responseString = await client.GetStringAsync(
        }
    }


    static void Main(string[] args)
    {   

        s_deviceClient = DeviceClient.CreateFromConnectionString(s_connectionString01, TransportType.Mqtt); 

        using I2cDevice i2c = I2cDevice.Create(new I2cConnectionSettings(1, 0x27));
        using var driver = new Pcf8574(i2c);
        using var lcd = new Lcd2004(registerSelectPin: 0, 
                                enablePin: 2, 
                                dataPins: new int[] { 4, 5, 6, 7 }, 
                                backlightPin: 3, 
                                backlightBrightness: 0.1f, 
                                readWritePin: 1, 
                                controller: new GpioController(PinNumberingScheme.Logical, driver));

        int pin = 18;
        using var controller = new GpioController();
        controller.OpenPin(pin, PinMode.Output);
        controller.OpenPin(17,PinMode.Output);
        bool ledOn = true;
        controller.Write(17,PinValue.Low);

        while(true){
            Menu(ledOn);

            Console.Write("Command:-> ");
            char userinput = Console.ReadLine()[0];
            Console.WriteLine("------------");

            if(userinput=='S'){
                Display();
            }
            else if(userinput=='H'){
                Hvacdisplay(ledOn);
                ledOn =!ledOn;
            }
            else if(userinput=='T'){
                
                SendDeviceToCloudMessagesAsync(s_deviceClient); 
                
                

            }
            else if(userinput=='I'){
                Console.WriteLine("Nun happened again :(");
            }
            else if(userinput=='X'){
                Console.WriteLine("Closing the RDTMS driver");
                controller.Write(pin, PinValue.Low);
                break;
            }
            else{

                Console.WriteLine("That is not an option >w<");
            }

        }
        
        lcd.Clear();

    }


    static void Menu(bool ledOn){

        Console.WriteLine("---------------");
        Console.WriteLine("RDTMS v1.0 Menu");
        Console.WriteLine("---------------");
        Console.WriteLine("'S': - Display current Temperature/humidity");
        if(ledOn)
            Console.WriteLine("'H': - Turn HVAC On");
        else
            Console.WriteLine("'H': - Turn HVAC Off");

        Console.WriteLine("'T': - Transmit Telemtry To Azure IoT Hub service");
        Console.WriteLine("'I': - Change telemtry transmit interval(1000ms -default)");
        Console.WriteLine("'X': - Close the RDTMS");

    }

    static void RealTime(){

        int pin = 17;
        using var controller = new GpioController();
        controller.OpenPin(pin, PinMode.Output);
        bool ledOn = true;

        var i2cSettings = new I2cConnectionSettings(1, Bme280.DefaultI2cAddress);
        using I2cDevice i2cDevice = I2cDevice.Create(i2cSettings);
        using var bme280 = new Bme280(i2cDevice);

        int measurementTime = bme280.GetMeasurementDuration();

        using I2cDevice i2c = I2cDevice.Create(new I2cConnectionSettings(1, 0x27));
        using var driver = new Pcf8574(i2c);
        using var lcd = new Lcd2004(registerSelectPin: 0, 
                                enablePin: 2, 
                                dataPins: new int[] { 4, 5, 6, 7 }, 
                                backlightPin: 3, 
                                backlightBrightness: 0.1f, 
                                readWritePin: 1, 
                                controller: new GpioController(PinNumberingScheme.Logical, driver));

        Console.WriteLine("Displaying current time. Press Ctrl+C to end.");


        while(true){

            lcd.Clear();

            bme280.SetPowerMode(Bmx280PowerMode.Forced);
            Thread.Sleep(measurementTime);

            bme280.TryReadTemperature(out var tempValue);
            bme280.TryReadPressure(out var preValue);
            bme280.TryReadHumidity(out var humValue);
            bme280.TryReadAltitude(out var altValue);
          
            lcd.SetCursorPosition(0,0);
            lcd.Write($"Temp: {tempValue.DegreesCelsius:0.#}\u00B0C");
            lcd.SetCursorPosition(0,1);
            lcd.Write($"Pres: {preValue.Hectopascals:#.##} hPa");
            lcd.SetCursorPosition(0,2);
            lcd.Write($"Hum: {humValue.Percent:#.##}%");
            lcd.SetCursorPosition(0,3);
            lcd.Write($"Alti: {altValue.Meters:#} m");

            controller.Write(pin, ((ledOn) ? PinValue.High : PinValue.Low));
            ledOn =!ledOn;
            
            Thread.Sleep(1000);

        }


    }

    static void Display(){

        var i2cSettings = new I2cConnectionSettings(1, Bme280.DefaultI2cAddress);
        using I2cDevice i2cDevice = I2cDevice.Create(i2cSettings);
        using var bme280 = new Bme280(i2cDevice);

        int measurementTime = bme280.GetMeasurementDuration();

        using I2cDevice i2c = I2cDevice.Create(new I2cConnectionSettings(1, 0x27));
        using var driver = new Pcf8574(i2c);
        using var lcd = new Lcd2004(registerSelectPin: 0, 
                                enablePin: 2, 
                                dataPins: new int[] { 4, 5, 6, 7 }, 
                                backlightPin: 3, 
                                backlightBrightness: 0.1f, 
                                readWritePin: 1, 
                                controller: new GpioController(PinNumberingScheme.Logical, driver));

        lcd.Clear();

        bme280.SetPowerMode(Bmx280PowerMode.Forced);
        Thread.Sleep(measurementTime);

        bme280.TryReadTemperature(out var tempValue);
        bme280.TryReadPressure(out var preValue);
        bme280.TryReadHumidity(out var humValue);
        bme280.TryReadAltitude(out var altValue);

        Console.WriteLine($"Temp: {tempValue.DegreesCelsius:0.#}\u00B0C");
        Console.WriteLine($"Pres: {preValue.Hectopascals:#.##} hPa");
        Console.WriteLine($"Hum: {humValue.Percent:#.##}%");
        Console.WriteLine($"Alti: {altValue.Meters:#} m");
        
        lcd.SetCursorPosition(0,0);
        lcd.Write($"Temp: {tempValue.DegreesCelsius:0.#}\u00B0C");
        lcd.SetCursorPosition(0,1);
        lcd.Write($"Pres: {preValue.Hectopascals:#.##} hPa");
        lcd.SetCursorPosition(0,2);
        lcd.Write($"Hum: {humValue.Percent:#.##}%");
        lcd.SetCursorPosition(0,3);
        lcd.Write($"Alti: {altValue.Meters:#} m");
            
        

        
    }

    static void Hvacdisplay(bool ledOn){

        int pin = 18;
        using var controller = new GpioController();
        controller.OpenPin(pin, PinMode.Output);

        using I2cDevice i2c = I2cDevice.Create(new I2cConnectionSettings(1, 0x27));
        using var driver = new Pcf8574(i2c);
        using var lcd = new Lcd2004(registerSelectPin: 0, 
                                enablePin: 2, 
                                dataPins: new int[] { 4, 5, 6, 7 }, 
                                backlightPin: 3, 
                                backlightBrightness: 0.1f, 
                                readWritePin: 1, 
                                controller: new GpioController(PinNumberingScheme.Logical, driver));

        lcd.Clear();
        lcd.SetCursorPosition(4,1);
        controller.Write(pin, ((ledOn) ? PinValue.High : PinValue.Low));
        if(ledOn)
            lcd.Write("HVAC ON");
        else
            lcd.Write("HVAC OFF");


    }

    private static async void SendDeviceToCloudMessagesAsync(DeviceClient s_deviceClient)  {  

        // Create an event processor client to process events in the event hub
        // TODO: Replace the <EVENT_HUBS_NAMESPACE> and <HUB_NAME> placeholder values

        int pin = 17;
        using var controller = new GpioController();
        controller.OpenPin(pin, PinMode.Output);
        bool ledOn=true;

        while(true){
             
                    
            var i2cSettings = new I2cConnectionSettings(1, Bme280.DefaultI2cAddress);
            using I2cDevice i2cDevice = I2cDevice.Create(i2cSettings);
            using var bme280 = new Bme280(i2cDevice);

            int measurementTime = bme280.GetMeasurementDuration();

            bme280.SetPowerMode(Bmx280PowerMode.Forced);
            Thread.Sleep(measurementTime);

            bme280.TryReadTemperature(out var tempValue);
            bme280.TryReadPressure(out var preValue);
            bme280.TryReadHumidity(out var humValue);
            bme280.TryReadAltitude(out var altValue);
            
            double currentTemperature = tempValue.DegreesFahrenheit;  
            double currentHumidity = humValue.Percent;

//          =========================================================
            await sendHTTP(currentTemperature,currentHumidity,ledOn);







//          =========================================================
            
            
            
            
            
            // Create JSON message  

            var telemetryDataPoint = new  
            {  
                temperature = currentTemperature,  
                humidity = currentHumidity  
            };  

            string messageString = "";  



            messageString = JsonConvert.SerializeObject(telemetryDataPoint);  

            var message = new Message(Encoding.ASCII.GetBytes(messageString));  

            // Add a custom application property to the message.  
            // An IoT hub can filter on these properties without access to the message body.  
            //message.Properties.Add("temperatureAlert", (currentTemperature > 30) ? "true" : "false");  
            controller.Write(pin, PinValue.High);

            // Send the telemetry message  
            await s_deviceClient.SendEventAsync(message);  
            Console.WriteLine("{0} > Sending message: {1}", DateTime.Now, messageString);  
            await Task.Delay(1000*1);  
            Thread.Sleep(2000);

            controller.Write(pin, PinValue.Low);



        }
    }  

    
    static void messageemail(){

        // This code retrieves your connection string from an environment variable.
        string connectionString = "endpoint=https://comsfordayss.unitedstates.communication.azure.com/;accesskey=6321lfYoXapAI2KFpraeboxgxpVLvACA9cY51WqidaSVWQEbO42tJQQJ99AGACULyCpAYtOZAAAAAZCStKcC";
        var emailClient = new EmailClient(connectionString);


        EmailSendOperation emailSendOperation = emailClient.Send(
        WaitUntil.Completed,
        senderAddress: "DoNotReply@b61ff559-7e1b-4f4d-a3e1-c6e8d1906f89.azurecomm.net",
        recipientAddress: "toledogoat82@gmail.com",
        subject: "Warning on HVAC Temp.",
        htmlContent: "<html><h1>Watch out it's spicy today</h1l></html>",
        plainTextContent: "Watch out it's spicy today");

    }

}   